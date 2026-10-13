#!/bin/sh
set -eu
umask 0002
exec dotnet /app/Digger.dll "$@"
