#!/bin/bash
set -e

export VITE_API_URL=/
printenv | grep VITE_API_URL

cd ../ChamboMetadataTaggerFrontend

# Temporarily disable .env so Vite picks up the shell environment variable
if [ -f .env ]; then
  mv .env .env.bak
  trap 'mv ../ChamboMetadataTaggerFrontend/.env.bak ../ChamboMetadataTaggerFrontend/.env' EXIT
fi

npm run build
cd -

cp -r ../ChamboMetadataTaggerFrontend/dist/* ./wwwroot/

export AZURE_CLI_DISABLE_CONNECTION_VERIFICATION=1
az acr build --registry mozdag --image chambo-metadata-tagger:latest --platform linux/amd64 .
