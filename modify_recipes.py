#!/usr/bin/env python
# -*- coding: utf-8 -*-
import os
import sys

# 文件路径
base_path = r"d:\_Unity\_UnityProject\FlatWorld\Assets\4_ScriptObjects\4-5_Cook"

# 需要修改的文件列表
files_to_modify = [
    "原木=木炭.asset",
    "生肉=熟肉.asset",
    "粗铁锭=铁锭.asset",
    "铁矿+碳=钢.asset",
    "铁矿=铁锭.asset",
    "铜+锡=青铜 1.asset",
    "铜+锡=青铜.asset",
    "铁矿=粗铁锭.asset",
    "铜矿=铜.asset",
    "锡矿=锡.asset",
    "鸡蛋=煎鸡蛋.asset",
]

modified_count = 0

for filename in files_to_modify:
    filepath = os.path.join(base_path, filename)
    
    if not os.path.exists(filepath):
        print("File not found: " + filename)
        continue
    
    try:
        # Read file
        with open(filepath, 'r', encoding='utf-8') as f:
            content = f.read()
        
        # Check if already has enableMirrorCrafting
        if 'enableMirrorCrafting' in content:
            print("Already exists: " + filename)
            continue
        
        # Replace: add "   enableMirrorCrafting: 1" before "action: []"
        new_content = content.replace('  action: []', '   enableMirrorCrafting: 1\n  action: []')
        
        # Check if replacement happened
        if new_content == content:
            print("NOT FOUND 'action: []': " + filename)
            continue
        
        # Write file
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(new_content)
        
        print("MODIFIED: " + filename)
        modified_count += 1
        
    except Exception as e:
        print("ERROR: " + filename + " - " + str(e))

print("\nTotal modified: " + str(modified_count) + " files")
