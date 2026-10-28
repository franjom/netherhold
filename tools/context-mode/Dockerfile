FROM node:22-bookworm-slim@sha256:de170b39439eb0e5a3444ad7d60b015614a381e10fecae2e30ec9e735122f38d

ARG CONTEXT_MODE_VERSION=1.0.169
LABEL org.opencontainers.image.source="https://github.com/mksglu/context-mode"

RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates git ripgrep python3 make g++ \
    && rm -rf /var/lib/apt/lists/*
RUN npm install --prefix /opt/context-mode --omit=dev --no-audit --no-fund \
    context-mode@${CONTEXT_MODE_VERSION}
RUN mkdir -p /data /workspace && chown node:node /data /workspace

ENV HOME=/data \
    CONTEXT_MODE_DIR=/data/context-mode \
    CONTEXT_MODE_PROJECT_DIR=/workspace
WORKDIR /workspace
USER node
ENTRYPOINT ["node", "/opt/context-mode/node_modules/context-mode/start.mjs"]
