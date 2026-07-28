SHELL := /usr/bin/env bash

COMPOSE_TEST_FILE := compose-test.yaml
LOAD_TESTS_DIR := scripts/load
LOCAL_LOAD_RUNNER := scripts/run-load-test.sh

export API_KEY BASE_URL RAC_ARGUMENTS_JSON RASGATE_API_KEY
export REQUEST_TIMEOUT STARTUP_ATTEMPTS STARTUP_DELAY_SECONDS
export SUMMARY_EXPORT TEST TEST_DURATION TEST_PAUSE_SECONDS TEST_VUS

.PHONY: help restore build test verify release clean load-run load-remote

help:
	@echo "Available commands:"
	@echo "  make restore                 — restore dependencies"
	@echo "  make build                   — build the solution"
	@echo "  make test                    — run tests"
	@echo "  make verify                  — restore, build, test, and create release archives"
	@echo "  make release                 — create all release archives"
	@echo "  make clean                   — remove build artifacts"
	@echo "  make load-run TEST=<name>    — run smoke, load, stress, or soak k6 test"
	@echo "  make load-remote             — run local k6 against a remote RasGate"

restore:
	dotnet restore

build: restore
	dotnet build --configuration Release --no-restore

test: build
	dotnet test --configuration Release --no-build --no-restore

verify: test release

release:
	./scripts/release.sh

clean:
	dotnet clean --configuration Debug
	dotnet clean --configuration Release
	rm -rf artifacts

load-run:
	@test -n "$(TEST)" || \
		{ echo "Usage: make load-run TEST=smoke"; exit 1; }
	@test -f "$(LOAD_TESTS_DIR)/$(TEST).js" || \
		{ echo "Load test not found: $(LOAD_TESTS_DIR)/$(TEST).js"; exit 1; }
	@set -e; \
	trap 'docker compose -f $(COMPOSE_TEST_FILE) down --remove-orphans' EXIT; \
	docker compose -f $(COMPOSE_TEST_FILE) up -d --build ras-gate; \
	docker compose -f $(COMPOSE_TEST_FILE) run --rm k6 \
			run "/scripts/$(TEST).js"

load-remote:
	@$(LOCAL_LOAD_RUNNER)
