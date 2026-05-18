using UnityEngine;
using System.Collections;

public interface IPieceAnimation
{
    IEnumerator Play(ChessPiece piece);
}
