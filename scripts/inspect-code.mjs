#!/usr/bin/env node

import { mkdtemp, readFile, rm } from "node:fs/promises"
import { tmpdir } from "node:os"
import { dirname, join, resolve } from "node:path"
import { fileURLToPath } from "node:url"
import { spawn } from "node:child_process"

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..")
const temporaryDirectory = await mkdtemp(join(tmpdir(), "sub2api-report-inspect-code-"))
const reportPath = join(temporaryDirectory, "inspect-code.sarif")
const cachesPath = join(temporaryDirectory, "caches")

try {
  const exitCode = await run("dotnet", [
    "tool",
    "run",
    "jb",
    "--",
    "inspectcode",
    "Sub2ApiReport.slnx",
    `--output=${reportPath}`,
    "--format=Sarif",
    "--severity=SUGGESTION",
    "--swea",
    "--no-build",
    "--no-updates",
    `--caches-home=${cachesPath}`,
    "--properties=Configuration=Release",
    "--verbosity=ERROR",
  ])

  if (exitCode !== 0) {
    throw new Error(`JetBrains InspectCode exited with code ${exitCode}. Run 'dotnet tool restore' and retry.`)
  }

  const report = JSON.parse(await readFile(reportPath, "utf8"))
  const issues = report.runs?.flatMap((run) => run.results ?? []) ?? []
  if (issues.length === 0) {
    console.log("JetBrains InspectCode: 0 issues at SUGGESTION or higher.")
    process.exitCode = 0
  } else {
    console.error(`JetBrains InspectCode: ${issues.length} issue(s) found.`)
    for (const issue of issues) {
      const location = issue.locations?.[0]?.physicalLocation
      const path = location?.artifactLocation?.uri ?? "unknown"
      const line = location?.region?.startLine ?? 0
      const message = issue.message?.text ?? issue.message?.markdown ?? "No description"
      console.error(`${path}:${line} [${issue.ruleId ?? "unknown"}] ${message}`)
    }

    process.exitCode = 1
  }
} finally {
  await rm(temporaryDirectory, { recursive: true, force: true })
}

function run(command, args) {
  return new Promise((resolveExitCode, reject) => {
    const child = spawn(command, args, {
      cwd: repositoryRoot,
      stdio: "inherit",
    })

    child.once("error", reject)
    child.once("exit", (code, signal) => {
      if (signal) {
        reject(new Error(`${command} terminated by signal ${signal}.`))
        return
      }

      resolveExitCode(code ?? 1)
    })
  })
}
