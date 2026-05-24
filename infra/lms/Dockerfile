FROM ubuntu:24.04

EXPOSE 1234

ENV PATH="/root/.lmstudio/bin:${PATH}"

RUN apt update && apt upgrade -y && apt install -y curl libatomic1

RUN curl -fsSL https://lmstudio.ai/install.sh | bash

ENTRYPOINT [ "lms", "server", "start" ]