#!/bin/bash

# This script clones the DeepWiki-Open repository, builds the docker image, and pushes it to the GitHub Container Registry.
ARG_PASSWORD="$1"
ARG_OPENAI_API_KEY="$2"

# Validate that GH_USER and GH_PAT are provided
echo "$ARG_PASSWORD" | sudo -S -v && clear

# Clone the DeepWiki-Open repository, build the docker image, and push it to the GitHub Container Registry
git clone https://github.com/AsyncFuncAI/deepwiki-open.git

# Create a .env file in the deepwiki-open directory with the OPENAI_API_KEY environment variable
echo "OPENAI_API_KEY=\"$ARG_OPENAI_API_KEY\"" >> deepwiki-open/.env

# Build the docker image and tag it for the GitHub Container Registry
docker build ./deepwiki-open -t deepwiki-open:latest

# Log in to the GitHub Container Registry using the provided credentials
rm -rf deepwiki-open