#!/bin/bash

export PATH="/root/.lmstudio/bin:${PATH}"

if ! command -v lms &> /dev/null; then
  apt update
  apt upgrade -y
  apt install -y curl libatomic1 libgomp1

  curl -fsSL https://lmstudio.ai/install.sh | bash

  lms get google/gemma-4-e4b
  lms load google/gemma-4-e4b
fi

lms daemon up
lms server start

# Keep the script running so the Docker container doesn't exit
tail -f /dev/null