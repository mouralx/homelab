#!/bin/bash

# Check if all required arguments are passed
if [ "$#" -lt 4 ] || [ "$#" -gt 5 ]; then
  echo "Usage: $0 <TOKEN> <SOURCE_REPO> <SOURCE_ENVIRONMENT> <TARGET_REPO> <TARGET_ENVIRONMENT>"
  echo "TOKEN is optional if already logged in"
  exit 1
fi

# Assign arguments to variables
if [ "$#" -eq 5 ]; then
  TOKEN=$1
  SOURCE_REPO=$2
  SOURCE_ENV=$3
  TARGET_REPO=$4
  TARGET_ENV=$5
else
  SOURCE_REPO=$1
  SOURCE_ENV=$2
  TARGET_REPO=$3
  TARGET_ENV=$4
fi

# Check if brew is installed
if ! command -v brew &> /dev/null; then
  echo "brew is not installed. Please install Homebrew and try again."
  exit 1
fi

# Install gh CLI if not installed
if ! command -v gh &> /dev/null; then
  echo "Installing gh CLI..."
  brew install gh
fi

# Install jq if not installed
if ! command -v jq &> /dev/null; then
  echo "Installing jq..."
  brew install jq
fi

# Login to GitHub if token provided
if [ -n "$TOKEN" ]; then
  echo "Logging in to GitHub CLI..."
  echo "$TOKEN" | gh auth login --with-token
fi

# Check if logged in
if ! gh auth status &> /dev/null; then
  echo "Not logged in to GitHub CLI. Please run 'gh auth login' or provide a TOKEN."
  exit 1
fi

# Fetch variables and secrets from the source repository and environment
source_vars=$(gh variable list --repo "$SOURCE_REPO" --env "$SOURCE_ENV" --json name,value)
source_secrets=$(gh secret list --repo "$SOURCE_REPO" --env "$SOURCE_ENV" --json name)

# Copy variables
jq -c '.[]' <<< "$source_vars" | while read -r var; do
  name=$(jq -r '.name' <<< "$var")
  value=$(jq -r '.value' <<< "$var")
  echo "Copying variable: $name"
  gh variable set $name --repo $TARGET_REPO --env $TARGET_ENV --body $value
done

# Copy secrets (Note: The actual secret values cannot be retrieved, so we use a placeholder)
jq -c '.[]' <<< "$source_secrets" | while read -r secret; do
  name=$(jq -r '.name' <<< "$secret")
  echo "Copying secret: $name"
  gh secret set $name --repo $TARGET_REPO --env $TARGET_ENV --body "REPLACE_WITH_SECRET_VALUE"
done