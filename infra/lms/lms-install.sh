#!/bin/bash

export PATH="/root/.lmstudio/bin:${PATH}"

apt update
apt upgrade -y
apt install -y libatomic1 curl

curl -fsSL https://lmstudio.ai/install.sh | bash

lms get google/gemma-4-e4b
lms load google/gemma-4-e4b
lms daemon up
lms start server