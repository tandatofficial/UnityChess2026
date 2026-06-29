using UnityEngine;
using Unity.Cinemachine;

public class CameraSwitcher : MonoBehaviour
{
    public static CameraSwitcher Instance;

    public CinemachineCamera camWhite;
    public CinemachineCamera camBlack;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        if (camWhite != null) camWhite.Priority = 0;
        if (camBlack != null) camBlack.Priority = 0;
    }

    public void SetCameraFor(PieceColor color)
    {
        if (color == PieceColor.White)
        {
            camWhite.Priority = 10;
            camBlack.Priority = 0;
        }
        else
        {
            camWhite.Priority = 0;
            camBlack.Priority = 10;
        }
    }
}
