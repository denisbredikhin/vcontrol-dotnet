# Copilot Instructions for This Workspace

## General
- This workspace starts **empty**. There are no predefined files or structure.
- The project will be developed **incrementally**, step by step, based on user prompts.
- Do NOT attempt to build the entire system at once unless explicitly requested.

## Goal
Create a solution that allows communicating with a Viessmann boiler
via Optolink (USB) using `vcontrold`, and later extend it with .NET 10 code
and possibly MQTT/Home Assistant integration.

The exact structure, files, and tooling should be created **as needed during the process**.

## Copilot behavior
- Explanations and conversation: **in Russian**.
- Code, filenames, Docker instructions, identifiers: **in English**.
- When asked to “start step X”, Copilot should:
  - decide what files or structure need to be created,
  - propose the simplest approach,
  - generate complete file contents,
  - if by some reason it can't generate files is should explain to the user how to do this manually
  - explain how to build/run/test them.

## Technical guidance
- Copilot may choose the most appropriate implementation path
  (e.g., building vcontrold in a Docker container, downloading sources,
  adding .NET projects, generating config files, etc.)
  depending on the step the user requests.

- Prefer clean, minimal solutions.

## Important
- If the user gives a vague prompt, ask for clarification.
- Never guess hidden requirements.
- Modify only what is explicitly requested in the current step.