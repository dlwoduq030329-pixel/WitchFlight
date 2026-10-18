using UnityEngine;

// Small code-drawn placeholders, cached by the HUD. Replaced by EmoteTable.icon when supplied.
public static class EmoteFallbackIcons
{
    public static Sprite Create(DefaultEmoteFace face)
    {
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        { name = "DefaultEmote_" + face, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        Color fill = face == DefaultEmoteFace.Hello ? new Color(.24f,.9f,.85f) :
            face == DefaultEmoteFace.Happy ? new Color(1f,.83f,.32f) :
            face == DefaultEmoteFace.Sad ? new Color(.42f,.67f,1f) : new Color(1f,.4f,.5f);
        Color ink = new Color(.07f,.1f,.17f);
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float px = (x + .5f - size / 2f) / (size * .46f);
            float py = (y + .5f - size / 2f) / (size * .46f);
            float r = Mathf.Sqrt(px*px + py*py);
            if (r > 1f) continue;
            bool eye = Circle(px+.32f,py-.2f,.085f) || Circle(px-.32f,py-.2f,.085f);
            bool mouth = Mathf.Abs(px) < .44f && Mathf.Abs(py - (-.38f + .8f*px*px)) < .035f;
            if (face == DefaultEmoteFace.Hello)
                eye = Circle(px+.32f,py-.2f,.085f) || (px > .19f && px < .45f && Mathf.Abs(py-.2f) < .035f);
            if (face == DefaultEmoteFace.Happy)
                mouth = Circle(px,(py+.26f)*1.25f,.36f) && py < -.18f;
            if (face == DefaultEmoteFace.Sad)
                mouth = Mathf.Abs(px) < .36f && Mathf.Abs(py - (-.23f - .9f*px*px)) < .035f;
            if (face == DefaultEmoteFace.Angry)
            {
                eye |= Mathf.Abs(px) > .14f && Mathf.Abs(px) < .48f && Mathf.Abs(py-(.22f+.4f*Mathf.Abs(px))) < .04f;
                mouth = Mathf.Abs(px) < .3f && Mathf.Abs(py+.28f) < .04f;
            }
            Color color = r > .94f || eye || mouth ? ink : fill;
            if (face == DefaultEmoteFace.Sad && Circle((px-.48f)*1.8f,py+.05f,.13f)) color = Color.white;
            color.a = Mathf.Clamp01((1f-r)*size*.46f);
            pixels[y*size+x] = color;
        }
        texture.SetPixels32(pixels); texture.Apply(false, true);
        return Sprite.Create(texture,new Rect(0,0,size,size),new Vector2(.5f,.5f),100f,0,SpriteMeshType.FullRect);
    }
    private static bool Circle(float x,float y,float radius) => x*x+y*y < radius*radius;
}
