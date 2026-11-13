#!/usr/bin/env bash
set -euo pipefail

repository_uri="$ECR_REGISTRY/$ECR_REPOSITORY"
known_image="$repository_uri@sha256:48be282d692341e1eb9acb68750321d8e6861b4227e0e505edb8095ee9ffa33d"
# Schema applied to RDS before the currently deployed PDF extraction image.
known_migrations=c7ab83950f0b26e82099c83a62eafd5a0f58c3c9
migrations_path=src/Tindy.Infrastructure/Persistence/Migrations
release_migrations=$(git rev-parse "$RELEASE_SHA:$migrations_path")

if [[ "$release_migrations" != "$known_migrations" ]]; then
  echo "::error::Migration files differ from the verified RDS baseline. Complete the backup and migration procedure before enabling this schema in CD."
  exit 1
fi

describe_service() {
  aws ecs describe-services --cluster "$ECS_CLUSTER" --services "$ECS_SERVICE" --output json
}

check_ready() {
  curl --fail --silent --show-error --max-time 10 --retry 3 \
    --header 'Cache-Control: no-cache' "$READY_URL" |
    jq -e '.status == "Healthy" and .service == "Tindy API"' >/dev/null
}

wait_for_revision() {
  local expected="$1" service
  aws ecs wait services-stable --cluster "$ECS_CLUSTER" --services "$ECS_SERVICE" || return 1
  service=$(describe_service) || return 1
  jq -e --arg revision "$expected" '
    .services[0] | .status == "ACTIVE" and .desiredCount > 0 and
    .taskDefinition == $revision and .runningCount == .desiredCount and
    (.deployments | length == 1 and .[0].rolloutState == "COMPLETED")
  ' <<< "$service" >/dev/null || return 1
  check_ready
}

service=$(describe_service)
if ! jq -e '
  .services[0] | .status == "ACTIVE" and .desiredCount > 0 and
  .runningCount == .desiredCount and
  (.deployments | length == 1 and .[0].rolloutState == "COMPLETED")
' <<< "$service" >/dev/null; then
  echo "::error::Tindy ECS is stopped, unavailable, or already deploying."
  exit 1
fi

previous_revision=$(jq -er '.services[0].taskDefinition' <<< "$service")
definition=$(aws ecs describe-task-definition --task-definition "$previous_revision" \
  --query taskDefinition --output json)
previous_image=$(jq -er '.containerDefinitions[] | select(.name == "api").image' <<< "$definition")
if [[ "$previous_image" != "$known_image" ]]; then
  previous_digest=${previous_image#"$repository_uri@"}
  if [[ ! "$previous_digest" =~ ^sha256:[a-f0-9]{64}$ ]]; then
    echo "::error::The running API image is not pinned to a digest in the Tindy ECR repository."
    exit 1
  fi
  previous_tags=$(aws ecr describe-images --repository-name "$ECR_REPOSITORY" \
    --image-ids "imageDigest=$previous_digest" --query 'imageDetails[0].imageTags' --output json)
  if ! jq -e --arg suffix "-$known_migrations" '
    any(.[]; test("^release-[a-f0-9]{40}-[a-f0-9]{40}$") and endswith($suffix))
  ' <<< "$previous_tags" >/dev/null; then
    echo "::error::The running image is outside the verified schema baseline. Reconcile the release before deploying."
    exit 1
  fi
fi
check_ready

tag="release-$RELEASE_SHA-$release_migrations"
if ! digest=$(aws ecr describe-images --repository-name "$ECR_REPOSITORY" \
  --image-ids "imageTag=$tag" --query 'imageDetails[0].imageDigest' --output text \
  2>"$RUNNER_TEMP/ecr-describe-error"); then
  if ! grep -q 'ImageNotFoundException' "$RUNNER_TEMP/ecr-describe-error"; then
    cat "$RUNNER_TEMP/ecr-describe-error" >&2
    exit 1
  fi
  aws ecr get-login-password | docker login --username AWS --password-stdin "$ECR_REGISTRY"
  docker tag "tindy-api:$RELEASE_SHA" "$repository_uri:$tag"
  docker push "$repository_uri:$tag"
  digest=$(aws ecr describe-images --repository-name "$ECR_REPOSITORY" \
    --image-ids "imageTag=$tag" --query 'imageDetails[0].imageDigest' --output text)
fi
[[ "$digest" =~ ^sha256:[a-f0-9]{64}$ ]]
image="$repository_uri@$digest"
printf 'Release commit: `%s`\n\nECR image: `%s`\n\nPrevious ECS revision: `%s`\n' \
  "$RELEASE_SHA" "$image" "$previous_revision" >> "$GITHUB_STEP_SUMMARY"

if [[ "$previous_image" == "$image" ]]; then
  echo 'This image is already deployed and ready.' >> "$GITHUB_STEP_SUMMARY"
  exit 0
fi

jq --arg image "$image" '
  del(.taskDefinitionArn, .revision, .status, .requiresAttributes, .compatibilities,
      .registeredAt, .registeredBy, .deregisteredAt) |
  (.containerDefinitions[] | select(.name == "api").image) = $image
' <<< "$definition" > "$RUNNER_TEMP/api-task-definition.json"
new_revision=$(aws ecs register-task-definition --cli-input-json "file://$RUNNER_TEMP/api-task-definition.json" \
  --query taskDefinition.taskDefinitionArn --output text)

current_revision=$(describe_service | jq -er '.services[0].taskDefinition')
if [[ "$current_revision" != "$previous_revision" ]]; then
  echo "::error::Another operation changed the ECS revision during preparation."
  exit 1
fi

if aws ecs update-service --cluster "$ECS_CLUSTER" --service "$ECS_SERVICE" \
     --task-definition "$new_revision" --query service.taskDefinition --output text &&
   wait_for_revision "$new_revision"; then
  printf '\nVerified ECS revision: `%s`\n' "$new_revision" >> "$GITHUB_STEP_SUMMARY"
  exit 0
fi

echo "::error::Backend rollout failed. Checking whether the previous revision needs to be restored."
current_revision=$(describe_service | jq -er '.services[0].taskDefinition')
if [[ "$current_revision" == "$new_revision" ]]; then
  aws ecs update-service --cluster "$ECS_CLUSTER" --service "$ECS_SERVICE" \
    --task-definition "$previous_revision" --query service.taskDefinition --output text
elif [[ "$current_revision" != "$previous_revision" ]]; then
  echo "::error::ECS now points to a different revision. Review the service before attempting rollback."
  exit 1
fi
if wait_for_revision "$previous_revision"; then
  printf '\nRollback verified: `%s`\n' "$previous_revision" >> "$GITHUB_STEP_SUMMARY"
else
  echo "::error::Could not verify rollback. Inspect the ECS service."
fi
exit 1
