FROM ubuntu:24.04

RUN apt-get update && apt-get install -y curl ca-certificates && rm -rf /var/lib/apt/lists/*

RUN curl -fsSL https://lmstudio.ai/install.sh | bash

ENV PATH="/root/.lmstudio/bin:${PATH}"

EXPOSE 1234

CMD sh -c "lms daemon up && lms server start --port 1234 --bind 0.0.0.0"