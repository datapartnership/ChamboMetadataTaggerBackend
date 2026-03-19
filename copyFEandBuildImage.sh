#!/bin/bash
cd ../ChamboMetadataTaggerFrontend

npm run build
cd -

cp -r ../ChamboMetadataTaggerFrontend/dist/* ./wwwroot/

export AZURE_CLI_DISABLE_CONNECTION_VERIFICATION=1
az acr build --registry mozdag --image chambo-metadata-tagger:latest --platform linux/amd64 .
