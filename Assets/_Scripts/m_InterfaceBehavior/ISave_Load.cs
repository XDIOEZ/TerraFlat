using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

public interface ISave_Load
{
    UltEvent onSave { get; set; }
    UltEvent onLoad { get; set; }

    void Save();
    void Load();
}
