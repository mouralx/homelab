#!/bin/bash

# Check if SUDO_PASSWORD argument is provided
if [ -z "$1" ]; then
    echo "Error: SUDO_PASSWORD argument is required"
    echo "Usage: $0 <sudo_password>"
    exit 1
fi

ARG_PASSWORD="$1"
ARG_GHUSER="$2"
ARG_GHPAT="$3"

echo "$ARG_PASSWORD" | sudo -S -v 

clear

git clone https://github.com/AsyncFuncAI/deepwiki-open.git

docker build ./deepwiki-open -t ghcr.io/home-lab/deepwiki-open:latest

echo $ARG_GHPAT | docker login ghcr.io -u $ARG_GHUSER --password-stdin

docker push ghcr.io/home-lab/deepwiki-open:latest

rm -rf deepwiki-open