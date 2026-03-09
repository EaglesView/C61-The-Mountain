@echo off
if defined GODOT_BIN (
    "%GODOT_BIN%" %*
) else (
    godot %*
)
