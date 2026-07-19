apt update -yq && apt install -yq curl libatomic1 libgomp1

curl -fsSL https://lmstudio.ai/install.sh | bash

~/.lmstudio/bin/lms get $LLM_MODEL

~/.lmstudio/bin/lms get $LLM_MODEL

~/.lmstudio/bin/lms load $LLM_MODEL

~/.lmstudio/bin/lms server start --port 4321 --bind 0.0.0.0

~/.lmstudio/bin/lms log stream