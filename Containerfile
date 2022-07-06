# based on https://github.com/poszu/godot-servers-k8s
ARG GODOT_VERSION=3.4.4
ARG GODOT_MONO_VERSION=mono-3.4.4
ARG GODOT_PROJECT_NAME
ARG GODOT_EXPORT_PRESET=Linux/X11

FROM barichello/godot-ci:$GODOT_MONO_VERSION as build
# Exports the Godot project into a .pck file
# Exporting requires an `export_presents.cfg` file in the root directory of the project.
ARG GODOT_EXPORT_PRESET

WORKDIR /src
COPY . .

RUN mkdir export && godot -v --export-pack ${GODOT_EXPORT_PRESET} export/server.pck --quit

FROM ubuntu:focal as godot-server
# Downloads the godot server binary

RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates \
    wget \
    unzip \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /bin

ARG GODOT_VERSION
RUN wget -c -q https://downloads.tuxfamily.org/godotengine/$GODOT_VERSION/mono/Godot_v$GODOT_VERSION-stable_mono_linux_server_64.zip \
    && unzip Godot_v$GODOT_VERSION-stable_mono_linux_server_64.zip \
    && mv Godot_v$GODOT_VERSION-stable_mono_linux_server_64 godot-server \ 
    && mv godot-server/Godot_v$GODOT_VERSION-stable_mono_linux_server.64 godot-server/server
#      > godot-server && chmod u+x godot-server

FROM ubuntu:focal
# The output docker image. Runs the godot server with the exported pck
ARG GODOT_PROJECT_NAME

WORKDIR /app
COPY --from=godot-server /bin/godot-server .
COPY --from=build /src/export .

RUN mkdir -p ~/.config/godot \
    && mkdir -p ~/.local/share/godot/app_userdata/${GODOT_PROJECT_NAME} # A workaround for https://github.com/godotengine/godot/issues/44873 to silence the error

ENTRYPOINT /app/server
