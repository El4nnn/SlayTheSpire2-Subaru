shader_type canvas_item;
render_mode unshaded;

// 死归黑幕里那张「嫉妒魔女」剪影的信号不稳效果。
// 时序全部按 0~1 的归一化进度 p 写，所以改停留时长不用动这个文件，
// 只要 RewindVisuals 把 u_total 传对新值就行。

uniform float u_u = 0.0;                       // 已经过去的时间（秒），由 Tween 推进
uniform float u_total = 1.584;                 // 时钟总长 = 淡入 0.10 + 停留 1.484
uniform vec2  u_res = vec2(1920.0, 1080.0);    // 画布像素尺寸

float hash11(float n) {
	return fract(sin(n * 78.233) * 43758.5453);
}

float hash21(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

// 频闪：黑场只占很短一段，紧接着的「撕裂恢复段」才是高频画面。
// 如果让撕裂和黑场同段，撕裂会全被黑吃掉。
float strobe_gain(float p) {
	float g = 1.0;
	if (p >= 0.120 && p < 0.150) g = min(g, 0.55);
	if (p >= 0.150 && p < 0.175) g = min(g, 0.02);
	if (p >= 0.330 && p < 0.360) g = min(g, 0.10);
	if (p >= 0.550 && p < 0.580) g = min(g, 0.45);
	if (p >= 0.580 && p < 0.610) g = min(g, 0.00);
	if (p >= 0.820 && p < 0.845) g = min(g, 0.30);
	return g;
}

float tear_amp(float p) {
	float a = 2.0;
	if (p >= 0.120 && p < 0.150) a = max(a, 30.0);
	if (p >= 0.175 && p < 0.250) a = max(a, 34.0);
	if (p >= 0.330 && p < 0.360) a = max(a, 8.0);
	if (p >= 0.360 && p < 0.450) a = max(a, 40.0);
	if (p >= 0.550 && p < 0.580) a = max(a, 12.0);
	if (p >= 0.610 && p < 0.700) a = max(a, 46.0);
	if (p >= 0.820 && p < 0.845) a = max(a, 10.0);
	if (p >= 0.845 && p < 0.900) a = max(a, 26.0);
	return a;
}

void fragment() {
	vec2 px = UV * u_res;
	float p = clamp(u_u / max(u_total, 0.001), 0.0, 1.0);
	float amp = tear_amp(p);

	// 撕裂带：每 ~0.034 s（按 u_total 缩放）换一次位置，做出「跳」的感觉
	float slot = floor(u_u * (29.0 / max(u_total, 0.001)));
	float xoff = 0.0;
	for (int i = 0; i < 3; i++) {
		float fi = float(i);
		float y0 = hash11(slot * 3.1 + fi * 7.7) * (u_res.y - 20.0);
		float hh = 8.0 + hash11(slot * 5.3 + fi * 3.3) * 62.0;
		float off = (hash11(slot * 9.7 + fi * 1.9) * 2.0 - 1.0) * amp;
		if (px.y >= y0 && px.y < y0 + hh) xoff += off;
	}

	vec2 uv = UV;
	uv.x += xoff / u_res.x;

	// 垂直失锁：只在最强那一段向下卷一点
	if (p >= 0.580 && p < 0.660) {
		uv.y += 72.0 * (p - 0.580) / 0.080 / u_res.y;
	}

	// 边缘 clamp 掉，避免撕裂把画面拖出边界时出现镜像/重复
	uv = clamp(uv, vec2(0.0), vec2(1.0));

	// 色差：R 右移、B 左移 3 px
	float ca = 3.0 / u_res.x;
	vec3 col;
	col.r = texture(TEXTURE, uv + vec2(ca, 0.0)).r;
	col.g = texture(TEXTURE, uv).g;
	col.b = texture(TEXTURE, uv - vec2(ca, 0.0)).b;

	// 频闪
	col *= strobe_gain(p);

	// 扫描线
	col *= 0.86 + 0.14 * sin(px.y * 3.14159265 / 2.5);

	// 下滚的亮同步条
	float bar_y = mod(u_u * 620.0 + 140.0, u_res.y + 160.0) - 80.0;
	if (px.y >= bar_y && px.y < bar_y + 34.0) {
		col += vec3(16.0 / 255.0);
	}

	// 颗粒（0~1 色彩空间，所以是 3.4/255）
	col += (hash21(px + vec2(slot * 13.0, slot * 7.0)) - 0.5) * (3.4 / 255.0);

	COLOR = vec4(clamp(col, vec3(0.0), vec3(1.0)), 1.0);
}
