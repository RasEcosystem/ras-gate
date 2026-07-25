SHELL := /usr/bin/env bash

.PHONY: help restore build test verify release clean

help:
	@echo "Available commands:"
	@echo "  make restore  — restore dependencies"
	@echo "  make build    — build the solution"
	@echo "  make test     — run tests"
	@echo "  make verify   — restore, build, test, and create release archives"
	@echo "  make release  — create all release archives"
	@echo "  make clean    — remove build artifacts"

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