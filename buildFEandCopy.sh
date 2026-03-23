#!/bin/bash
cd ../ChamboMetadataTaggerFrontend

npm run build
cd -

cp -r ../ChamboMetadataTaggerFrontend/dist/* ./wwwroot/