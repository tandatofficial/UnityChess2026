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
        Vector3 originalPos = piece.transform.position;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float offsetX = Mathf.Sin(elapsed * 50f) * strength;
            piece.transform.position = originalPos + new Vector3(offsetX, 0, 0);
            elapsed += Time.deltaTime;
            yield return null;
        }

        piece.transform.position = originalPos;
    }
}
