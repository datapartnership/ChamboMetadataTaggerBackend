#!/usr/bin/env bash

# Usage: source loadenv.sh path/to/.env

ENV_FILE="$1"

if [[ -z "$ENV_FILE" ]]; then
  echo "Usage: source loadenv.sh <env-file>"
  return 1 2>/dev/null || exit 1
fi

if [[ ! -f "$ENV_FILE" ]]; then
  echo "Error: File '$ENV_FILE' not found."
  return 1 2>/dev/null || exit 1
fi

# Read each line, handling missing final newline and CRLF endings
while IFS= read -r line || [[ -n "$line" ]]; do
  # Strip trailing CR (for files with Windows line endings)
  line=${line%$'\r'}

  # Skip empty lines and comments (leading whitespace allowed)
  [[ -z "$line" || "$line" =~ ^[[:space:]]*# ]] && continue

  # Split on first '='
  key=${line%%=*}
  value=${line#*=}

  # Trim whitespace around key and value
  key=${key##[[:space:]]}
  key=${key%%[[:space:]]}
  value=${value##[[:space:]]}
  value=${value%%[[:space:]]}

  # Remove surrounding single/double quotes if present
  if [[ "$value" =~ ^\".*\"$ ]]; then
    value=${value#\"}
    value=${value%\"}
  elif [[ "$value" =~ ^\'.*\'$ ]]; then
    value=${value#\'}
    value=${value%\'}
  fi

  # Export only if key and value are both non-empty
  [[ -n "$key" && -n "$value" ]] && export "$key=$value"
done < "$ENV_FILE"

echo "Environment variables loaded from $ENV_FILE"
