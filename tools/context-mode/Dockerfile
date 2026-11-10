FROM node:24-bookworm-slim@sha256:01d566e8d94d8c619591d78652babfccd3c6992a3dffa9ed5ab39e11cf6c70e9

ARG CONTEXT_MODE_VERSION=2.1.7
LABEL org.opencontainers.image.source="https://github.com/mksglu/context-mode"

# 2.x is a dependency-free bundle on Node's built-in node:sqlite (needs Node >= 24.15),
# so no native toolchain is installed.
RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates git ripgrep python3 \
    && rm -rf /var/lib/apt/lists/*
RUN npm install --prefix /opt/context-mode --omit=dev --no-audit --no-fund \
    context-mode@${CONTEXT_MODE_VERSION}
RUN mkdir -p /data /workspace && chown node:node /data /workspace
# The bind-mounted project is owned by another uid; without this git (and so 2.x
# project resolution) rejects it as "dubious ownership". The mount is read-only.
RUN git config --system --add safe.directory /workspace

ENV HOME=/data \
    CONTEXT_MODE_DIR=/data/context-mode \
    CONTEXT_MODE_PROJECT_DIR=/workspace
WORKDIR /workspace
USER node
# 2.x refuses to resolve a project without an agent id. "codex" derives the project
# from the working directory (/workspace) and assumes no host hooks or plugin.
ENTRYPOINT ["node", "/opt/context-mode/node_modules/context-mode/server.mjs", "--agent", "codex"]
