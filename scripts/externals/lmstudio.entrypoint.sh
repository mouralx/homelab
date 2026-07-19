#!/bin/bash

if [ ! -f ~/.lmstudio/bin/lms ]; then

    apt update -yq && apt install -yq curl libatomic1 libgomp1

    curl -fsSL https://lmstudio.ai/install.sh | bash

    ~/.lmstudio/bin/lms get $LLM_MODEL

    ~/.lmstudio/bin/lms get $LLM_MODEL

    ~/.lmstudio/bin/lms load $LLM_MODEL --context-length 128000
    
fi

~/.lmstudio/bin/lms server start --port 4321 --bind 0.0.0.0

~/.lmstudio/bin/lms log stream