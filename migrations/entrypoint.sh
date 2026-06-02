#!/bin/sh
set -e

if [ "$USE_AZURE_AUTH" = "true" ]; then
    token=$(wget -qO- \
        --post-data "grant_type=client_credentials&client_id=${AZURE_CLIENT_ID}&client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer&client_assertion=$(cat $AZURE_FEDERATED_TOKEN_FILE)&scope=https://ossrdbms-aad.database.windows.net/.default" \
        "https://login.microsoftonline.com/${AZURE_TENANT_ID}/oauth2/v2.0/token" \
        | sed -n 's/.*"access_token":"\([^"]*\)".*/\1/p')

    export DATABASE_URL="${DATABASE_URL}&password=${token}"
fi

exec migrate -path /migrations -database "${DATABASE_URL}" up
