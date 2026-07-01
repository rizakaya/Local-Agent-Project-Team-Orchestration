# Team Protocol

Use a leader-managed workflow for local model collaboration.

## Roles and Models

- Project Lead: coordinate work and make next-step decisions. Default model: `qwen3.6:latest`.
- Project Analyst: produce requirements, scope, assumptions, acceptance criteria, and open questions. Default model: `qwen3.6:latest`.
- Project Developer: produce .NET implementation plans and scoped code changes. Default model: `qwen3-coder:30b`.
- Project Tester: produce test plans, risk checks, and acceptance validation. Default model: `gemma3:12b`.

## Handoff Rules

- Start with analysis before implementation.
- Keep every handoff explicit: sender, receiver, input, expected output.
- Mark assumptions clearly and avoid treating them as confirmed facts.
- Use concise artifacts that the next role can act on.
- End each role output with a clear readiness state: `ready`, `blocked`, or `needs-clarification`.

## First Delivery Boundary

The first delivery is the project team skill setup only. The .NET communication application is a later implementation phase.
