using System.Drawing;
internal sealed class PetCardPalette {
    public Color Accent,Ink,Muted,Track,Background,Panel,Selected,Border;
    static Color Mix(Color a,Color b,float amount){return Color.FromArgb((int)(a.R+(b.R-a.R)*amount),(int)(a.G+(b.G-a.G)*amount),(int)(a.B+(b.B-a.B)*amount));}
    public static PetCardPalette From(Color accent){return new PetCardPalette{Accent=accent,Ink=Mix(accent,Color.FromArgb(15,23,36),.72F),Muted=Mix(accent,Color.FromArgb(105,114,130),.67F),Track=Mix(accent,Color.White,.82F),Background=Mix(accent,Color.White,.965F),Panel=Mix(accent,Color.White,.925F),Selected=Mix(accent,Color.White,.84F),Border=Mix(accent,Color.White,.63F)};}
}
