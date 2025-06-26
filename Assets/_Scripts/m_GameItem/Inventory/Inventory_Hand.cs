using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Inventory_Hand : Inventory
{
    public float GetItemAmountRate = 1;
    /*
    //获取Inventory组件
    public Inventory inventory;
    //是否获取一半
    public bool isGetHalf = false;

    public bool isGetOne = false;



    public void Awake()
    {
        inventory = GetComponent<Inventory>();
    }
    private void Start()
    {
    }

    public void Update()
    {
        if (Input.GetKeyDown(KeyCode.LeftShift))
        {
            GetItemAmountRate = 0.5f;
        }
        else if (Input.GetKeyUp(KeyCode.LeftShift))
        {
            GetItemAmountRate = 1;
        }

        if (Input.GetKeyDown(KeyCode.LeftControl))
        {
            GetItemAmountRate = 0.001f;
        }
        else if (Input.GetKeyUp(KeyCode.LeftControl))
        {
            GetItemAmountRate = 1;
        }
    }*/

    public override void OnItemClick(int index)
    {

    }
}
