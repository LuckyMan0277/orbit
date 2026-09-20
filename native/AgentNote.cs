using System;
using System.Collections.Generic;
using System.Linq;
namespace Orbit {
 // The short guide an AI CLI receives when Orbit starts it, so the user never has to explain the secret vault. It holds key names only,
 // never values. Claude reads it from a file (--append-system-prompt-file); Codex takes it as one config value (-c developer_instructions=...),
 // so that text is a single line of plain characters that survives PowerShell and cmd argument handling: no quotes, %, &, |, <, >, ^ or $.
 internal static class AgentNote {
  // Which terminals receive the stored keys on their own. A plain shell does not (it may be a throwaway one); it uses "orbit-secret run".
  public static bool GetsKeys(string profile) { return profile=="claude"||profile=="codex"; }
  public static string Claude(IEnumerable<string> names) {
   return String.Join("\r\n",new[]{
    "# Orbit secrets",
    "",
    "This terminal runs inside Orbit, which keeps the user's API keys for you. The user cannot paste keys into this chat, and you must never see their values.",
    "",
    "Environment variables already set in this terminal: "+List(names),
    "",
    "Rules",
    "- Read a key from the environment in code (process.env.NAME, os.environ[\"NAME\"], $env:NAME). Never print, log, echo or write a key value anywhere: not to the terminal, files, .env files, config or commits. Do not run printenv, env, set or echo on them.",
    "- If you need a key that is not listed, run: orbit-secret request NAME \"why you need it\". Orbit shows the user a private prompt and tells you only whether it was saved. Do not ask the user to paste the key.",
    "- A key saved after this session started is not in this shell's environment. Run commands that need it as: orbit-secret run -- <command> (for example: orbit-secret run -- node app.js).",
    "- orbit-secret list shows every stored key name.",
    ""});
  }
  // Codex hides environment variables whose names contain KEY, SECRET or TOKEN from the commands it runs, so it is told to always go through
  // orbit-secret run, which works whether or not the variable reached its shell.
  public static string Codex(IEnumerable<string> names) {
   return "Orbit secrets: this terminal runs inside Orbit, which stores API keys for the user. The user cannot paste keys into this chat and you must never see their values. "
    +"Stored key names: "+List(names)+". "
    +"Rules: 1) Run every command that needs a key as: orbit-secret run -- COMMAND (for example: orbit-secret run -- node app.js). Orbit puts the keys into that command environment; in code read them from the environment (process.env.NAME or os.environ NAME). "
    +"2) Never print, log, echo or write a key value anywhere: not to the terminal, files, .env files, config or commits, and do not run printenv, env, set or echo on a key. "
    +"3) If you need a key that is not stored, run: orbit-secret request NAME followed by a short reason. Orbit shows the user a private prompt and tells you only whether it was saved. Do not ask the user to paste the key. "
    +"4) orbit-secret list shows every stored key name.";
  }
  static string List(IEnumerable<string> names) { var list=names.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();return list.Length==0?"none yet":String.Join(", ",list); }
 }
}
