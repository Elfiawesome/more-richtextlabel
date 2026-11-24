@tool
class_name MoreRichTextLabelGD
extends Control

const ROW_ALIGN_TOP := 0
const ROW_ALIGN_MIDDLE := 1
const ROW_ALIGN_BOTTOM := 2

## GDScript implementation using one RichTextLabel per text block
## and a custom vertical block layout for [panel] / [margin].

var _text := ""
var _default_panel_style: StyleBox
var _selection_enabled := false
var _trim_block_whitespace := true
var _custom_effects: Array[RichTextEffect] = []
var _row_alignment := ROW_ALIGN_TOP
var _placed_views: Array[RichTextLabel] = []
var _split_views := {}

@export_multiline
var text: String:
	get:
		return _text
	set(value):
		_text = value
		if is_inside_tree():
			rebuild()

@export
var default_panel_style: StyleBox:
	get:
		return _default_panel_style
	set(value):
		_default_panel_style = value
		if is_inside_tree():
			_relayout()
			queue_redraw()

@export
var selection_enabled: bool:
	get:
		return _selection_enabled
	set(value):
		_selection_enabled = value
		for node in _text_nodes:
			if is_instance_valid(node.view):
				_configure_leaf_input(node.view)

		for node in _split_views.values():
			for view in node:
				if is_instance_valid(view):
					_configure_leaf_input(view)

@export
var trim_block_whitespace: bool:
	get:
		return _trim_block_whitespace
	set(value):
		_trim_block_whitespace = value
		if is_inside_tree():
			rebuild()

@export
var custom_effects: Array[RichTextEffect]:
	get:
		return _custom_effects
	set(value):
		_custom_effects = value if value != null else []
		if is_inside_tree():
			rebuild()

@export_enum("Top", "Middle", "Bottom")
var row_alignment: int:
	get:
		return _row_alignment
	set(value):
		_row_alignment = value
		if is_inside_tree():
			_relayout()

var _root: Array = []
var _text_nodes: Array = []
var _panels: Array = []
var _content_height := 0.0
var _in_layout := false
var _empty_style := StyleBoxEmpty.new()
var _connected_styles: Array[StyleBox] = []
var _preferred_text_widths := {}

# Parser state.
var _src := ""
var _pos := 0
var _buffer := ""

static var _style_cache := {}


class DocBlock:
	var kind := 0 # 0 text, 1 panel, 2 margin
	var markup := ""
	var style: StyleBox
	var children: Array = []
	var insets := Vector4.ZERO
	var view: RichTextLabel
	var rect := Rect2()


func _ready() -> void:
	rebuild()


func _exit_tree() -> void:
	_disconnect_style_signals()
	_in_layout = false


func set_markup(value: String) -> void:
	text = value


func rebuild() -> void:
	if not is_inside_tree():
		return

	for node in _text_nodes:
		var view: RichTextLabel = node.view
		if is_instance_valid(view):
			remove_child(view)
			view.free()
		node.view = null

	_text_nodes.clear()
	_preferred_text_widths.clear()

	for node in _split_views.values():
		for view in node:
			if is_instance_valid(view):
				remove_child(view)
				view.free()

	_split_views.clear()
	_root = _parse(text)
	_create_views(_root)
	_relayout()


func _has_invalid_views() -> bool:
	for node in _text_nodes:
		if not is_instance_valid(node.view):
			return true

	for node in _split_views.values():
		for view in node:
			if not is_instance_valid(view):
				return true

	return false


func _relayout_deferred() -> void:
	if not is_inside_tree():
		return

	_in_layout = false

	if _root.is_empty() or _has_invalid_views():
		rebuild()
		return

	_relayout()


func _rebuild_deferred() -> void:
	if is_inside_tree():
		rebuild()

func force_relayout() -> void:
	_relayout()


func force_relayout_at_width(width: float) -> void:
	_relayout(width)


func _create_views(sequence: Array) -> void:
	for block in sequence:
		if block.kind == 0:
			_create_leaf(block)
		else:
			_create_views(block.children)


func _create_leaf(block: DocBlock) -> void:
	var leaf := RichTextLabel.new()
	leaf.bbcode_enabled = true
	leaf.fit_content = false
	leaf.scroll_active = false
	leaf.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	leaf.clip_contents = false
	leaf.add_theme_stylebox_override("normal", _empty_style)

	for effect in custom_effects:
		if effect != null:
			leaf.install_effect(effect)

	_configure_leaf_input(leaf)
	add_child(leaf, false, Node.INTERNAL_MODE_FRONT)
	leaf.text = block.markup
	block.view = leaf
	_text_nodes.append(block)


func _configure_leaf_input(leaf: RichTextLabel) -> void:
	leaf.selection_enabled = selection_enabled
	leaf.mouse_filter = Control.MOUSE_FILTER_STOP if selection_enabled else Control.MOUSE_FILTER_IGNORE
	leaf.focus_mode = Control.FOCUS_CLICK if selection_enabled else Control.FOCUS_NONE


func _relayout(width_override: float = -1.0) -> void:
	if _in_layout or _root.is_empty():
		return

	if _has_invalid_views():
		call_deferred("rebuild")
		return

	_in_layout = true
	_panels.clear()
	_placed_views.clear()
	_hide_all_text_views()

	var width := width_override
	if width <= 0.0:
		width = size.x
	if is_nan(width) or width <= 0.0:
		width = 1.0

	_content_height = _layout_sequence(_root, 0.0, 0.0, width)

	_in_layout = false

	_update_style_connections()
	update_minimum_size()
	queue_redraw()


func _hide_all_text_views() -> void:
	for node in _text_nodes:
		if is_instance_valid(node.view):
			node.view.visible = false

	for node in _split_views.values():
		for view in node:
			if is_instance_valid(view):
				view.visible = false


func _get_split_views(block: DocBlock) -> Array:
	if _split_views.has(block):
		return _split_views[block]

	var views := [_create_aux_leaf(), _create_aux_leaf()]
	_split_views[block] = views
	return views


func _create_aux_leaf() -> RichTextLabel:
	var leaf := RichTextLabel.new()
	leaf.bbcode_enabled = true
	leaf.fit_content = false
	leaf.scroll_active = false
	leaf.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	leaf.clip_contents = false
	leaf.add_theme_stylebox_override("normal", _empty_style)
	_configure_leaf_input(leaf)
	add_child(leaf, false, Node.INTERNAL_MODE_FRONT)
	leaf.visible = false
	return leaf


func _get_view_preferred_width(view: RichTextLabel) -> float:
	if not is_instance_valid(view):
		return 0.0

	var previous_autowrap: int = view.autowrap_mode
	var previous_size := view.size
	view.autowrap_mode = TextServer.AUTOWRAP_OFF
	view.size = Vector2(100000.0, 1.0)
	var preferred := float(view.get_content_width())
	view.autowrap_mode = previous_autowrap
	view.size = previous_size
	return preferred


func _place_text_view(view: RichTextLabel, x: float, y: float, width: float) -> float:
	if not is_instance_valid(view):
		return 0.0
	view.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	view.position = Vector2(x, y)
	view.size = Vector2(width, 1.0)
	var height := float(view.get_content_height())
	view.size = Vector2(width, height)
	view.visible = true
	_placed_views.append(view)
	return height


func _try_split_text(block: DocBlock, x: float, width: float, row_items: Array, line_x: float, cursor_y: float, line_height: float) -> Array:
	var words := _split_words(block.markup)
	if words.size() < 2:
		return [false, line_x, cursor_y, line_height, true]

	var views := _get_split_views(block)
	var head: RichTextLabel = views[0]
	var tail: RichTextLabel = views[1]

	var remaining := x + width - line_x
	if remaining <= 0.0:
		return [false, line_x, cursor_y, line_height, true]

	var head_text := ""
	var head_width := 0.0
	var fit_count := 0

	for i in words.size():
		var candidate: String = head_text + words[i]
		head.text = candidate
		var candidate_width := _get_view_preferred_width(head)
		if candidate_width > remaining:
			break
		head_text = candidate
		head_width = candidate_width
		fit_count += 1

	if fit_count == 0 or fit_count >= words.size():
		return [false, line_x, cursor_y, line_height, true]

	head.text = head_text
	var head_text_start := _placed_views.size()
	var head_panel_start := _panels.size()
	var head_height := _place_text_view(head, line_x, cursor_y, head_width)
	row_items.append([head_text_start, _placed_views.size(), head_panel_start, _panels.size(), head_height])

	var row_height := maxf(line_height, head_height)
	_apply_row_alignment(row_items, row_height)
	row_items.clear()
	cursor_y += row_height
	line_x = x
	line_height = 0.0

	var tail_text := ""
	for i in range(fit_count, words.size()):
		tail_text += words[i]
	tail_text = _trim_start_spaces(tail_text)

	if tail_text.is_empty():
		return [true, line_x, cursor_y, line_height, false]

	tail.text = tail_text
	var tail_width := minf(_get_view_preferred_width(tail), width)
	if tail_width <= 0.01:
		tail_width = width

	var tail_text_start := _placed_views.size()
	var tail_panel_start := _panels.size()
	var tail_height := _place_text_view(tail, x, cursor_y, tail_width)
	row_items.append([tail_text_start, _placed_views.size(), tail_panel_start, _panels.size(), tail_height])
	line_x = x + tail_width
	line_height = tail_height
	return [true, line_x, cursor_y, line_height, true]


func _trim_start_spaces(value: String) -> String:
	var start := 0
	while start < value.length() and (value.unicode_at(start) == 32 or value.unicode_at(start) == 9):
		start += 1
	return value.substr(start)

func _layout_sequence(sequence: Array, x: float, y: float, width: float) -> float:
	var cursor_y := y
	var line_x := x
	var line_height := 0.0
	var has_line := false
	var row_items: Array = []

	for block in sequence:
		if block.kind == 0 and _is_block_text(block):
			if has_line:
				_apply_row_alignment(row_items, line_height)
				row_items.clear()
				cursor_y += line_height
				line_x = x
				line_height = 0.0
				has_line = false
			cursor_y += _place_text(block, x, cursor_y, width)
			continue

		var preferred := _get_preferred_width(block)
		var item_width := minf(preferred, width)

		if item_width <= 0.01:
			continue

		if has_line and line_x + item_width > x + width:
			if block.kind == 0 and _can_split_plain_text(block.markup):
				var split_result := _try_split_text(block, x, width, row_items, line_x, cursor_y, line_height)
				if split_result[0]:
					line_x = split_result[1]
					cursor_y = split_result[2]
					line_height = split_result[3]
					has_line = split_result[4]
					continue

			_apply_row_alignment(row_items, line_height)
			row_items.clear()
			cursor_y += line_height
			line_x = x
			line_height = 0.0
			has_line = false

		var text_start := _placed_views.size()
		var panel_start := _panels.size()
		var item_height := _place_block(block, line_x, cursor_y, item_width)
		row_items.append([text_start, _placed_views.size(), panel_start, _panels.size(), item_height])
		line_x += item_width
		line_height = maxf(line_height, item_height)
		has_line = true

	if has_line:
		_apply_row_alignment(row_items, line_height)
		cursor_y += line_height

	return cursor_y - y


func _apply_row_alignment(row_items: Array, line_height: float) -> void:
	for item in row_items:
		var offset := _get_row_offset(item[4], line_height)
		if offset == 0.0:
			continue

		for i in range(item[0], item[1]):
			var view: RichTextLabel = _placed_views[i]
			if is_instance_valid(view):
				view.position = view.position + Vector2(0.0, offset)

		for i in range(item[2], item[3]):
			var panel: DocBlock = _panels[i]
			var rect := panel.rect
			rect.position.y += offset
			panel.rect = rect


func _get_row_offset(item_height: float, line_height: float) -> float:
	if _row_alignment == ROW_ALIGN_MIDDLE:
		return (line_height - item_height) * 0.5
	if _row_alignment == ROW_ALIGN_BOTTOM:
		return line_height - item_height
	return 0.0


func _place_block(block: DocBlock, x: float, y: float, width: float) -> float:
	if block.kind == 0:
		return _place_text(block, x, y, width)

	if block.kind == 2:
		var content_width := maxf(1.0, width - block.insets.x - block.insets.z)
		var content_height := _layout_sequence(block.children, x + block.insets.x, y + block.insets.y, content_width)
		return content_height + block.insets.y + block.insets.w

	var style := _get_panel_style(block)
	var left := style.get_margin(SIDE_LEFT)
	var top := style.get_margin(SIDE_TOP)
	var right := style.get_margin(SIDE_RIGHT)
	var bottom := style.get_margin(SIDE_BOTTOM)
	var content_width := maxf(1.0, width - left - right)

	block.rect = Rect2(x, y, width, 0.0)
	_panels.append(block)

	var content_height := _layout_sequence(block.children, x + left, y + top, content_width)
	var height := content_height + top + bottom
	block.rect = Rect2(x, y, width, height)
	return height


func _place_text(block: DocBlock, x: float, y: float, width: float) -> float:
	var view := block.view
	if not is_instance_valid(view):
		return 0.0
	return _place_text_view(view, x, y, width)

func _get_preferred_width(block: DocBlock) -> float:
	if block.kind == 0:
		return _get_text_preferred_width(block)

	if block.kind == 2:
		return block.insets.x + _get_sequence_preferred_width(block.children) + block.insets.z

	var style := _get_panel_style(block)
	return style.get_margin(SIDE_LEFT) + _get_sequence_preferred_width(block.children) + style.get_margin(SIDE_RIGHT)


func _get_sequence_preferred_width(sequence: Array) -> float:
	var width := 0.0
	for block in sequence:
		width += _get_preferred_width(block)
	return width


func _get_text_preferred_width(block: DocBlock) -> float:
	if _preferred_text_widths.has(block):
		return _preferred_text_widths[block]

	var view := block.view
	if not is_instance_valid(view):
		return 0.0

	var preferred := _get_view_preferred_width(view)
	_preferred_text_widths[block] = preferred
	return preferred


func _is_block_text(block: DocBlock) -> bool:
	return block.markup.contains("\n") or block.markup.contains("\r")

func _get_panel_style(block: DocBlock) -> StyleBox:
	if block.style != null:
		return block.style
	if default_panel_style != null:
		return default_panel_style
	return _empty_style


func _update_style_connections() -> void:
	var needed: Array[StyleBox] = []

	if default_panel_style != null:
		needed.append(default_panel_style)

	for block in _panels:
		var style := _get_panel_style(block)
		if style != _empty_style and not needed.has(style):
			needed.append(style)

	for style in _connected_styles.duplicate():
		if not needed.has(style):
			if is_instance_valid(style) and style.changed.is_connected(_on_style_changed):
				style.changed.disconnect(_on_style_changed)
			_connected_styles.erase(style)

	for style in needed:
		if not _connected_styles.has(style):
			style.changed.connect(_on_style_changed)
			_connected_styles.append(style)


func _disconnect_style_signals() -> void:
	for style in _connected_styles:
		if is_instance_valid(style) and style.changed.is_connected(_on_style_changed):
			style.changed.disconnect(_on_style_changed)
	_connected_styles.clear()


func _on_style_changed() -> void:
	if not is_inside_tree():
		return
	_relayout()
	queue_redraw()


func _draw() -> void:
	for block in _panels:
		_get_panel_style(block).draw(get_canvas_item(), block.rect)


func _get_minimum_size() -> Vector2:
	return Vector2(0.0, _content_height)


func _notification(what: int) -> void:
	if what == NOTIFICATION_THEME_CHANGED:
		_preferred_text_widths.clear()
		call_deferred("_relayout_deferred")
	elif what == NOTIFICATION_RESIZED:
		_relayout()
	elif what == NOTIFICATION_ENTER_TREE:
		_in_layout = false
		if is_node_ready():
			call_deferred("_rebuild_deferred")
	elif what == NOTIFICATION_EXIT_TREE:
		_in_layout = false
	elif what == NOTIFICATION_VISIBILITY_CHANGED:
		if is_visible_in_tree():
			call_deferred("_relayout_deferred")


# --- Parser ---------------------------------------------------------------

func _parse(source: String) -> Array:
	_src = source
	_pos = 0
	_buffer = ""
	return _parse_sequence("")


func _parse_sequence(stop_tag: String) -> Array:
	var children: Array = []

	while _pos < _src.length():
		var open := _src.find("[", _pos)
		if open < 0:
			_buffer += _src.substr(_pos)
			_pos = _src.length()
			break

		if open > _pos:
			_buffer += _src.substr(_pos, open - _pos)
			_pos = open

		var close := _src.find("]", _pos + 1)
		if close < 0:
			_buffer += _src.substr(_pos)
			_pos = _src.length()
			break

		var raw := _src.substr(_pos, close - _pos + 1)
		var inner := _src.substr(_pos + 1, close - _pos - 1)
		_pos = close + 1

		var tag := _parse_tag(inner)
		var name: String = tag[0]
		var closing: bool = tag[1]
		var value: String = tag[2]

		if name == "panel" or name == "margin":
			if closing:
				if name == stop_tag:
					_flush_text(children)
					return children
				_buffer += raw
				continue

			_flush_text(children)
			var block := DocBlock.new()

			if name == "panel":
				block.kind = 1
				block.style = _load_style(value)
				block.children = _parse_sequence("panel")
			else:
				block.kind = 2
				block.insets = _parse_margin(value)
				block.children = _parse_sequence("margin")

			children.append(block)
			continue

		_buffer += raw

	_flush_text(children)
	return children



func _can_split_plain_text(markup: String) -> bool:
	if markup.contains("[") or markup.contains("\n") or markup.contains("\r"):
		return false

	var has_word := false
	var has_whitespace := false

	for i in markup.length():
		var code := markup.unicode_at(i)
		if code == 32 or code == 9:
			has_whitespace = true
		else:
			has_word = true

	return has_word and has_whitespace


func _split_words(markup: String) -> Array:
	var words: Array = []
	var i := 0

	while i < markup.length():
		var start := i
		while i < markup.length() and (markup.unicode_at(i) == 32 or markup.unicode_at(i) == 9):
			i += 1

		var word_start := i
		while i < markup.length() and markup.unicode_at(i) != 32 and markup.unicode_at(i) != 9:
			i += 1

		if i > word_start:
			words.append(markup.substr(start, i - start))
		elif i == markup.length() and words.size() > 0 and start < i:
			words[words.size() - 1] += markup.substr(start)

	return words

func _flush_text(children: Array) -> void:
	var raw := _buffer
	_buffer = ""

	if trim_block_whitespace:
		raw = _trim_newline_edges(raw)

	if raw.is_empty():
		return

	var block := DocBlock.new()
	block.kind = 0
	block.markup = raw
	children.append(block)


func _parse_tag(inner: String) -> Array:
	var closing := inner.begins_with("/")
	var start := 1 if closing else 0
	var end := start

	while end < inner.length() and inner.unicode_at(end) != 61 and inner.unicode_at(end) > 32:
		end += 1

	var name := inner.substr(start, end - start).strip_edges().to_lower()
	var value := ""
	if not closing:
		var equals := inner.find("=")
		if equals >= 0:
			value = inner.substr(equals + 1).strip_edges()

	return [name, closing, value]


func _trim_newline_edges(value: String) -> String:
	var start := 0
	var finish := value.length()

	while start < finish:
		var start_code := value.unicode_at(start)
		if start_code != 9 and start_code != 10 and start_code != 13:
			break
		start += 1

	while finish > start:
		var end_code := value.unicode_at(finish - 1)
		if end_code != 9 and end_code != 10 and end_code != 13:
			break
		finish -= 1

	return value.substr(start, finish - start)


func _parse_margin(value: String) -> Vector4:
	if value.is_empty():
		return Vector4.ZERO

	var parts := value.split(",", false)
	var numbers: Array[float] = []
	for part in parts:
		if part.strip_edges().is_valid_float():
			numbers.append(part.strip_edges().to_float())
		else:
			return Vector4.ZERO

	if numbers.size() == 1:
		var n := numbers[0]
		return Vector4(n, n, n, n)
	if numbers.size() == 2:
		return Vector4(numbers[0], numbers[1], numbers[0], numbers[1])
	if numbers.size() == 4:
		return Vector4(numbers[0], numbers[1], numbers[2], numbers[3])
	return Vector4.ZERO


func _load_style(path: String) -> StyleBox:
	if path.is_empty():
		return null

	if _style_cache.has(path):
		return _style_cache[path]

	var style := load(path) as StyleBox
	_style_cache[path] = style
	return style
