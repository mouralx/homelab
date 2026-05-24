FROM ubuntu:24.04

EXPOSE 1234

WORKDIR /lms

ENV PATH="/root/.lmstudio/bin:${PATH}"

RUN apt update --quiet && apt install -y curl libatomic1 libgomp --quiet

RUN curl -fsSL https://lmstudio.ai/install.sh | bash

RUN lms get google/gemma-4-e4b

RUN lms load google/gemma-4-e4b

RUN lms daemon up

CMD ["lms", "server", "start"]