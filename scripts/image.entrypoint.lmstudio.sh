#!/bin/bash

if [ ! -f ~/.lmstudio/bin/lms ]; then

    curl -fsSL https://lmstudio.ai/install.sh | bash

    ~/.lmstudio/bin/lms get $LLM_MODEL

    ~/.lmstudio/bin/lms get $LLM_MODEL

    ~/.lmstudio/bin/lms load $LLM_MODEL --context-length 64000

    ~/.lmstudio/bin/lms get $EMBEDDING_MODEL
    
fi

~/.lmstudio/bin/lms server start --port 4321 --bind 0.0.0.0

~/.lmstudio/bin/lms log stream
