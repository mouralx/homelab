#!/bin/bash

ARG_PASSWORD="$1"

# Validate that GH_USER and GH_PAT are provided
echo "$ARG_PASSWORD" | sudo -S -v && clear

# Clone the DeepWiki-Open repository, build the docker image, and push it to the GitHub Container Registry
git clone https://github.com/AsyncFuncAI/deepwiki-open.git

# Build the docker image and tag it for the GitHub Container Registry
docker build ./deepwiki-open -t deepwiki-open:latest

# Log in to the GitHub Container Registry using the provided credentials
rm -rf deepwiki-open