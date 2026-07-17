# last-target-game

Multi-app repo for 2D shooter game experiments. Each `appNN_*/` directory is a self-contained app/prototype — see its own `CLAUDE.md` for stack and conventions specific to it.

## Apps
- `app01_cpp_UNITY/` — Unity (C#) client + authoritative C++ dedicated server. See `app01_cpp_UNITY/CLAUDE.md`.

Keep this root file short and stack-agnostic. Put design docs, PRDs, and SSDLC plans in their own `.md` files (referenced, not duplicated), and put stack-specific rules in the nested `CLAUDE.md` of the app they apply to.
