using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Collections;

public class CardPreviewBattleInputHandler : MonoSingleton<CardPreviewBattleInputHandler>
{
    public delegate void OnMouseInField(Vector3 fieldPosition);   //필드 위 터치 완료
    public delegate void OnMouseUpCallback(Vector3 position);
    public delegate void OnDragCallback(Vector3 position, bool isOnField);
    public delegate void OnDragCancel();

    public delegate void SpawnUnitCallback(Vector3 position, bool useCommand, int aDegree);

    private SpawnUnitCallback mSpawnUnitCallback;

    public OnMouseInField onMouseInField { get; set; }
    public OnMouseUpCallback onMouseUp { get; set; }

    public OnDragCallback onDragCallback { get; set; }
    public OnDragCancel onDragCancelCallback { get; set; }

    private bool mDragStart = false;
    private Vector3 mLastDragPos;

    private LayerMask mInputMask;
    private Camera mActiveCamera;
    private Vector3 mOffset;

    public void Initialize(Camera cam)
    {
        mInputMask = (int)eLayerMask.FIELD;
        mActiveCamera = cam;
        mOffset = new Vector3(0, GameSetting.Instance.CardUseTargetHeight, 0);
    }

    private bool mMouseDown = false;
    private Vector3 mMousePosition;

    private void Update()
    {
        if (mActiveCamera == null)
            return;

        if (Input.GetMouseButtonDown(0))
        {
            //mDragStart = true;
            mMouseDown = true;
            mLastDragPos = Vector3.one * int.MaxValue;
            mMousePosition = Input.mousePosition;
        }
        else if (Input.GetMouseButtonUp(0))
        {
            Ray ray = mActiveCamera.ScreenPointToRay(Input.mousePosition + (mDragStart ? mOffset : Vector3.zero));
            if (Physics.Raycast(ray, out RaycastHit hit, float.MaxValue, mInputMask))
            {
                Vector3 vPos = hit.point;
                //드래그를 안하고 바로 내려놓았을 경우에 필트 드래그 처리 1번 호출
                if (!mDragStart)
                {
                    onDragCallback?.Invoke(vPos, true);
                }
                onMouseInField?.Invoke(vPos);
                onMouseUp?.Invoke(vPos);
            }
            else
            {
                if (mDragStart)
                {
                    onDragCancelCallback?.Invoke();
                }
                onMouseUp?.Invoke(Input.mousePosition);
            }
            mDragStart = false;
            mMouseDown = false;
        }
        else if (mMouseDown && !mDragStart)
        {
            if (mMousePosition != Input.mousePosition)
            {
                mDragStart = true;
            }
            mMousePosition = Input.mousePosition;
        }
        else
        {
            if (mDragStart)
            {
                Ray ray = mActiveCamera.ScreenPointToRay(Input.mousePosition + mOffset);
                if (Physics.Raycast(ray, out RaycastHit hit, float.MaxValue, mInputMask))
                {
                    mLastDragPos = hit.point;
                    mLastDragPos.y += 0.1f;
                    onDragCallback?.Invoke(mLastDragPos, true);
                }
                else
                {
                    onDragCallback?.Invoke(mLastDragPos, false);
                }
            }
        }
    }
}
