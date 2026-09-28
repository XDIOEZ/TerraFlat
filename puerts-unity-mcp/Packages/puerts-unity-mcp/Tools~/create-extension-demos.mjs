#!/usr/bin/env node
import { ensureExtensionDemos, getToolRoots, parseArgs, resolveUnityProjectRoot } from "./pum-cli-lib.mjs";

const roots = getToolRoots(import.meta.url);
const args = parseArgs();
const projectRoot = resolveUnityProjectRoot(args, roots.toolsRoot);
ensureExtensionDemos(projectRoot, { toolsRoot: roots.toolsRoot });
