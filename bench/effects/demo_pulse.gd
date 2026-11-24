@tool
extends RichTextEffect
class_name DemoPulseEffect

## Demo custom effect. Add this resource to MoreRichTextLabel.CustomEffects
## and use [demo_pulse]text[/demo_pulse].

var bbcode = "demo_pulse"


func _process_custom_fx(char_fx: CharFXTransform) -> bool:
	var pulse := 0.5 + 0.5 * sin(char_fx.elapsed_time * 4.0 + char_fx.relative_index * 0.35)
	char_fx.color = Color(1.0, 0.55 + 0.35 * pulse, 0.15, char_fx.color.a)
	return true
