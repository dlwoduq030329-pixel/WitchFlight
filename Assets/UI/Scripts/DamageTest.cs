using Lovatto.Crosshair;
using System.Collections;
using UnityEngine;
using UnityEngine.Timeline;


public class DamageTest : MonoBehaviour
{
    // 인스펙터에서 bl_Crosshair가 있는 오브젝트를 연결
    public bl_HitMarker hitMarker;
    // 테스트용으로 가상의 적 위치를 지정할 변수
    public Vector3 fakeEnemyPosition = new Vector3(10f, 0f, 10f);

    // 버튼에 연결할 실제 함수
    public void TestButtonClick()
    {
        // static 메서드이므로 어디서든 바로 호출이 가능합니다.
        DamageIndicator.SetDamageIndicator(fakeEnemyPosition, Color.red);
        Debug.Log("데미지 표시기 테스트 호출 완료!");
    }

    // 1. 사격(크로스헤어 애니메이션) 테스트 함수
    public void TestFire()
    {
        //hitMarker.OnHit();
        //hitMarker.OnHit("default");
        hitMarker.OnHit("letal");
    }
}
