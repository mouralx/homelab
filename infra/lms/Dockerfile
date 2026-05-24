FROM ubuntu:24.04

EXPOSE 1234

ENV PATH="/root/.lmstudio/bin:${PATH}"

RUN apt update && apt install -y curl libatomic1

RUN curl -fsSL https://lmstudio.ai/install.sh | bash

# RUN lms daemon up

ENTRYPOINT [ "lms", "server", "start" ]