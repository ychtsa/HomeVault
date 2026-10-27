#!/usr/bin/env bash
# Permit only this App Service plan's possible egress addresses, not all Azure services.
set -euo pipefail
: "${AZURE_RESOURCE_GROUP:?Required}" "${AZURE_WEBAPP_NAME:?Required}" "${AZURE_SQL_SERVER:?Required}"
addresses=$(az webapp show --resource-group "$AZURE_RESOURCE_GROUP" --name "$AZURE_WEBAPP_NAME" \
  --query possibleOutboundIpAddresses --output tsv)
if [[ -z "$addresses" || "$addresses" == None ]]; then
  echo "App Service has no outbound addresses; retry after resource provisioning finishes." >&2
  exit 1
fi
while IFS= read -r address; do
  python3 -c 'import ipaddress,sys; ipaddress.IPv4Address(sys.argv[1])' "$address"
  az sql server firewall-rule create --resource-group "$AZURE_RESOURCE_GROUP" --server "$AZURE_SQL_SERVER" \
    --name "homevault-${address//./-}" --start-ip-address "$address" --end-ip-address "$address" --output none
done < <(tr ',' '\n' <<< "$addresses")
