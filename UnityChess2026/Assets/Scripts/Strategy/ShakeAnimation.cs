using UnityEngine;
using System.Collections;

public class ShakeAnimation : IPieceAnimation
{
    private float duration;
    private float strength;

    public ShakeAnimation(float duration = 0.2f, float strength = 0.1f)
    {
        this.duration = duration;
        this.strength = strength;
    }

    public IEnumerator Play(ChessPiece piece)
    {
        if (piece == null) yield break;

        Transform pieceTransform = piece.transform;
        if (pieceTransform == null) yield break;

        Vector3 originalPos = pieceTransform.position;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (pieceTransform == null) yield break;

            float offsetX = Mathf.Sin(elapsed * 50f) * strength;
            pieceTransform.position = originalPos + new Vector3(offsetX, 0, 0);
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (pieceTransform != null)
        {
            pieceTransform.position = originalPos;
        }
    }
}
