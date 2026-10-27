#!/usr/bin/env bash
# Read-only checks. Never create or upgrade a resource to work around missing free capacity.
set -euo pipefail
: "${AZURE_RESOURCE_GROUP:?Required}" "${AZURE_WEBAPP_NAME:?Required}"
: "${AZURE_SQL_SERVER:?Required}" "${AZURE_SQL_DATABASE:?Required}"

plan_id=$(az webapp show --resource-group "$AZURE_RESOURCE_GROUP" --name "$AZURE_WEBAPP_NAME" --query serverFarmId --output tsv)
sku=$(az appservice plan show --ids "$plan_id" --query sku.name --output tsv)
if [[ "$sku" != F1 ]]; then
  echo "Deployment stopped: App Service must use the free F1 plan (found $sku)." >&2
  exit 1
fi

az sql db show --resource-group "$AZURE_RESOURCE_GROUP" --server "$AZURE_SQL_SERVER" \
  --name "$AZURE_SQL_DATABASE" --query '{free:useFreeLimit,behavior:freeLimitExhaustionBehavior}' --output json |
  python3 -c 'import json,sys; d=json.load(sys.stdin); sys.exit(0 if d.get("free") is True and d.get("behavior")=="AutoPause" else "Deployment stopped: SQL free offer and AutoPause are required; overage billing is forbidden.")'
echo "Verified F1 and SQL free offer with AutoPause."
