# Provenance and licensing notes

- `src/HsAuto/Assets/LegendRush.png` is the artwork supplied by the user, copied unchanged. `LegendRush.ico` is a seven-resolution Windows icon conversion of that same image. The artwork is not covered by this project's MIT code licence; verify its permission separately before public distribution.
- Version 0.1.1 is branded **Hs LegendRush**. The existing local-data directory and named-pipe protocol identifiers are intentionally preserved for compatibility. The maintained TraditionalControls/Automation/UI code is not intended to be regenerated with the initial SourceSlice tool.

- `src/HsAuto.Core/Migrated/*` is a dependency slice reconstructed from the user's supplied HsAuto 0.8.21 source. It is included to preserve the user's local recommendation/state/action behavior; the slice intentionally excludes the old GUI, card-key service, token-login code, updater/watchdog, and legacy licence path.
- `src/HsAuto.Bridge` is a new local Bridge implementation derived from the migrated behavior and uses a separate protocol/pipe name. It contains no licence check, login-hook, anti-cheat patch, hardware spoofing, remote control, or traffic capture logic.
- No code from the referenced HearthstoneLegendArriver repository was copied into this project. A legal review of external project licenses is outside this build.
- `Data/hdt-by-id.json` is runtime card data copied from the user's local package. Treat it as third-party/runtime data; it is not covered by this project's MIT notice.
- `BepInEx.Runtime` in the portable package is the official Windows x64 runtime used only to host the user's locally installed Bridge. See `licenses/` for notices. The game itself and its managed DLLs are not distributed.

- Official BepInEx 5.4.23.5 core/bootstrap DLLs were compared against the supplied package and are byte-identical. Mono core-library overrides and Doorstop configuration are carried forward from the user-supplied runtime; their exact upstream build was not independently established. See licenses/runtime-provenance.json. Public redistribution should verify these assets separately.
- Unity Doorstop 4.5.0 carries LGPL-2.1; the local study bundle retains the unmodified binary and full upstream licence, and can be replaced independently. Before a public release, verify all source-offer/notice obligations against the exact upstream source/release. The source ZIP itself carries no Doorstop/Mono/game DLL binaries.

- MIT copyright coverage for migrated user-owned code relies on the user's authorization/ownership statement. No independent copyright-chain audit was performed. Exact notices for all runtime assets must be verified before public distribution.

0.1.2 新增的路径发现、绑定证据策略和缺推荐诊断仅处理本机实例及状态，不携带账户鉴权资料。测试只采用隔离 fixture，不把真实日志或账户数据打入包。
