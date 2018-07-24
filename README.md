# UnityEncrypt
>该项目简单展示了对Unity编译后的托管dll进行加壳和解密的整体过程。
## XXteaWin
>winform项目，该项目直接对dll进行加壳

## mono-unity-2017.3
>clone自mono-unity-2017.3分支，并在基础上加入了加解密方法，使得mono在load Assembly-CSharp.dll时自动解密执行。  
>这个版本不固定，可以切换到其他分支，主要看开发环境对应的unity版本，将本项目中的mono\metadata\xxtea.c、mono\metadata\xxtea.h复制到对应分支，并修改image.c文件中的mono_image_open_from_data_with_name方法加入解密方法。

## 使用方法
1.打包后的unity项目中Data\Mono\EmbedRuntime\mono.dll需要用上面项目mono-unity-2017.3编译后的dll来替换；
2.Data\Managed\Assembly-CSharp.dll也需要用加壳后的dll替换。
