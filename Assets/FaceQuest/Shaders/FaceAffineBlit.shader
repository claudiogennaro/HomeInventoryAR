// Copia una regione della texture della camera applicando una trasformazione
// affine, direttamente sulla GPU.
//
// Perche serve: l'allineamento di ArcFace non e un ritaglio rettangolare, e una
// similarita (rotazione + scala + traslazione) che porta i cinque landmark del
// volto sui cinque punti canonici del template 112x112. Farlo in CPU
// significherebbe leggere ogni fotogramma dalla GPU: un blocco di millisecondi
// per volto. Qui e un blit.
//
// Lo stesso shader serve anche per il ridimensionamento all'ingresso del
// detector, con la matrice identita scalata: un solo materiale per due usi.
//
// _Row0 e _Row1 sono le due righe della matrice INVERSA (dal pixel di
// destinazione al pixel sorgente, entrambi con origine in alto a sinistra).

Shader "FaceQuest/AffineBlit"
{
    Properties
    {
        _MainTex ("Sorgente", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Row0;       // (a, b, tx, -)
            float4 _Row1;       // (c, d, ty, -)
            float4 _SrcTexel;   // (1/larghezzaSorgente, 1/altezzaSorgente, -, -)
            float4 _DstSize;    // (larghezzaDest, altezzaDest, -, -)

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Dal UV di destinazione al pixel di destinazione, con l'asse y
                // rivoltato: qui si ragiona sempre in "pixel dall'alto".
                float2 d = float2(i.uv.x * _DstSize.x, (1.0 - i.uv.y) * _DstSize.y);

                float xs = _Row0.x * d.x + _Row0.y * d.y + _Row0.z;
                float ys = _Row1.x * d.x + _Row1.y * d.y + _Row1.z;

                float2 uvs = float2(xs * _SrcTexel.x, 1.0 - ys * _SrcTexel.y);

                // Fuori dalla sorgente si restituisce nero invece di ripetere il
                // bordo: un volto sul margine del frame non deve generare pixel
                // inventati che l'embedding poi prende per veri.
                if (uvs.x < 0.0 || uvs.x > 1.0 || uvs.y < 0.0 || uvs.y > 1.0)
                    return fixed4(0, 0, 0, 1);

                return tex2D(_MainTex, uvs);
            }
            ENDCG
        }
    }
}
