targetScope = 'resourceGroup'

@description('Lowercase letters/digits only; names receive a stable resource-group suffix.')
@minLength(3)
@maxLength(9)
param prefix string = 'homevault'
param location string = resourceGroup().location

@description('Exact GitHub OIDC sub claim for the production environment. See deployment guide for immutable subject format.')
param githubOidcSubject string

@description('Object ID of the human or group that will bootstrap contained SQL users.')
param sqlAdminObjectId string
param sqlAdminName string
@allowed(['User', 'Group'])
param sqlAdminPrincipalType string = 'User'

// This deployment deliberately cannot select a paid App Service tier.
@allowed(['F1'])
param appServiceSku string = 'F1'
@description('Public GHCR image; publish it and make the package public before deployment.')
param containerImage string = 'ghcr.io/ychtsa/homevault:bootstrap'
param databaseName string = 'HomeVault'

var suffix = uniqueString(resourceGroup().id)
var name = '${prefix}-${suffix}'
var tags = { application: 'HomeVault' }
var readerRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'acdd72a7-3385-48ef-bd42-f606fba81ae7')
var websiteContributorRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'de139f84-1756-47ae-9be6-808fbbe84772')

resource runtimeIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${name}-app'
  location: location
  tags: tags
}

resource deployIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${name}-github'
  location: location
  tags: tags
}

resource githubFederation 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = {
  parent: deployIdentity
  name: 'github-production'
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    subject: githubOidcSubject
    audiences: ['api://AzureADTokenExchange']
  }
}

resource sql 'Microsoft.Sql/servers@2023-08-01' = {
  name: '${name}-sql'
  location: location
  tags: tags
  properties: {
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: sqlAdminPrincipalType
      login: sqlAdminName
      sid: sqlAdminObjectId
      tenantId: tenant().tenantId
      azureADOnlyAuthentication: true
    }
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sql
  name: databaseName
  location: location
  tags: tags
  sku: { name: 'GP_S_Gen5_2', tier: 'GeneralPurpose', family: 'Gen5', capacity: 2 }
  properties: {
    // Exhaustion pauses the database. Never fall back to overage billing.
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    autoPauseDelay: 60
    minCapacity: json('0.5')
    maxSizeBytes: 34359738368
    requestedBackupStorageRedundancy: 'Local'
  }
}

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${name}-plan'
  location: location
  tags: tags
  kind: 'linux'
  sku: { name: appServiceSku, capacity: 1 }
  properties: { reserved: true }
}

resource app 'Microsoft.Web/sites@2024-04-01' = {
  name: name
  location: location
  tags: tags
  kind: 'app,linux,container'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${runtimeIdentity.id}': {} }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      alwaysOn: false
      linuxFxVersion: 'DOCKER|${containerImage}'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      http20Enabled: true
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
        { name: 'ASPNETCORE_HTTP_PORTS', value: '8080' }
        { name: 'WEBSITES_PORT', value: '8080' }
        { name: 'WEBSITES_ENABLE_APP_SERVICE_STORAGE', value: 'true' }
        { name: 'WEBSITES_CONTAINER_START_TIME_LIMIT', value: '300' }
        { name: 'DOCKER_REGISTRY_SERVER_URL', value: 'https://ghcr.io' }
        { name: 'AZURE_CLIENT_ID', value: runtimeIdentity.properties.clientId }
        // F1 includes persistent /home storage. No billable Blob account is created.
        { name: 'Storage__Provider', value: 'FileSystem' }
        { name: 'Storage__FileSystemRoot', value: '/home/uploads' }
        { name: 'DataProtection__KeysPath', value: '/home/keys' }
        { name: 'Database__ApplyMigrations', value: 'false' }
        { name: 'ForwardedHeaders__Enabled', value: 'true' }
        { name: 'ForwardedHeaders__TrustAllProxies', value: 'true' }
        { name: 'Swagger__Enabled', value: 'false' }
        {
          name: 'ConnectionStrings__DefaultConnection'
          value: 'Server=tcp:${sql.properties.fullyQualifiedDomainName},1433;Database=${database.name};Authentication=Active Directory Managed Identity;User Id=${runtimeIdentity.properties.clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30'
        }
      ]
    }
  }
}

resource disableScmPassword 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: app
  name: 'scm'
  properties: { allow: false }
}

resource disableFtpPassword 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: app
  name: 'ftp'
  properties: { allow: false }
}

resource deployWebsite 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: app
  name: guid(app.id, deployIdentity.id, websiteContributorRole)
  properties: {
    principalId: deployIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: websiteContributorRole
  }
}

// The runner can maintain firewall rules, but cannot change SQL administrators or databases.
resource firewallRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' = {
  name: guid(resourceGroup().id, 'HomeVault SQL Firewall Manager')
  properties: {
    roleName: 'HomeVault SQL Firewall Manager ${suffix}'
    description: 'Read SQL server details and maintain its firewall rules for migrations and App Service egress.'
    type: 'CustomRole'
    assignableScopes: [resourceGroup().id]
    permissions: [{
      actions: ['Microsoft.Sql/servers/read', 'Microsoft.Sql/servers/databases/read', 'Microsoft.Sql/servers/firewallRules/*']
      notActions: []
      dataActions: []
      notDataActions: []
    }]
  }
}

resource deployFirewall 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sql
  name: guid(sql.id, deployIdentity.id, firewallRole.id)
  properties: {
    principalId: deployIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: firewallRole.id
  }
}

output appUrl string = 'https://${app.properties.defaultHostName}/app/'
output runtimePrincipalId string = runtimeIdentity.properties.principalId
output deployPrincipalId string = deployIdentity.properties.principalId
output sqlServerFqdn string = sql.properties.fullyQualifiedDomainName
output githubVariables object = {
  AZURE_CLIENT_ID: deployIdentity.properties.clientId
  AZURE_TENANT_ID: tenant().tenantId
  AZURE_SUBSCRIPTION_ID: subscription().subscriptionId
  AZURE_RESOURCE_GROUP: resourceGroup().name
  AZURE_WEBAPP_NAME: app.name
  AZURE_SQL_SERVER: sql.name
  AZURE_SQL_DATABASE: database.name
}

resource deployPlanReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: plan
  name: guid(plan.id, deployIdentity.id, readerRole)
  properties: {
    principalId: deployIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: readerRole
  }
}
