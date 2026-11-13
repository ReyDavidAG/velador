use crate::engine::{Camera, Summary};

pub fn span(seconds: i64) -> String {
    let s = seconds.max(0);
    if s < 60 {
        return format!("{s} s");
    }
    if s < 3600 {
        return format!("{} min", s / 60);
    }
    let (h, m) = (s / 3600, (s % 3600) / 60);
    if m == 0 {
        format!("{h} h")
    } else {
        format!("{h} h {m} min")
    }
}

pub fn cause(code: &str) -> &'static str {
    match code {
        "sleep" => "el equipo estuvo en reposo",
        "camera" => "sin señal de la cámara",
        "network" => "la red se cayó",
        "engine" => "el motor se reinició",
        "power" => "corte de luz",
        "update" => "actualización",
        "disk" => "disco lleno",
        "paused" => "guardia en pausa",
        "privacy" => "modo privacidad",
        "credentials" => "la cámara rechazó la cuenta",
        _ => "sin grabación",
    }
}

pub fn status(summary: &Result<Summary, String>) -> String {
    let Ok(summary) = summary else {
        return "El motor no responde".into();
    };
    let Some(camera) = summary.cameras.first() else {
        return "Sin cámara configurada".into();
    };
    camera_status(camera, summary.now_ms)
}

pub fn camera_status(camera: &Camera, now_ms: i64) -> String {
    match (camera.state.as_str(), &camera.gap, camera.guard_since_ms) {
        ("on_guard", _, Some(since)) => {
            format!("De guardia · {} sin huecos", span((now_ms - since) / 1000))
        }
        ("on_guard", _, None) => "De guardia".into(),
        (_, Some(gap), _) => format!(
            "Sin grabar hace {} · {}",
            span((now_ms - gap.start_ms) / 1000),
            cause(&gap.cause)
        ),
        _ => "Reconectando con la cámara".into(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::engine::OpenGap;

    fn camera(state: &str, gap: Option<OpenGap>, since: Option<i64>) -> Camera {
        Camera {
            id: "entrada".into(),
            name: "Entrada".into(),
            state: state.into(),
            gap,
            guard_since_ms: since,
        }
    }

    #[test]
    fn los_lapsos_se_leen_como_en_la_web() {
        assert_eq!(span(42), "42 s");
        assert_eq!(span(125), "2 min");
        assert_eq!(span(7200), "2 h");
        assert_eq!(span(7260), "2 h 1 min");
        assert_eq!(span(-5), "0 s");
    }

    #[test]
    fn el_estado_dice_guardia_hueco_o_reconexion() {
        assert_eq!(
            camera_status(&camera("on_guard", None, Some(0)), 7_260_000),
            "De guardia · 2 h 1 min sin huecos"
        );
        let gap = OpenGap {
            start_ms: 0,
            cause: "camera".into(),
        };
        assert_eq!(
            camera_status(&camera("gap", Some(gap), None), 96_000),
            "Sin grabar hace 1 min · sin señal de la cámara"
        );
        assert_eq!(
            camera_status(&camera("reconnecting", None, None), 0),
            "Reconectando con la cámara"
        );
    }

    #[test]
    fn sin_motor_o_sin_camara_tambien_se_dice() {
        assert_eq!(status(&Err("x".into())), "El motor no responde");
        let empty = Summary {
            now_ms: 0,
            state: "no_camera".into(),
            cameras: vec![],
        };
        assert_eq!(status(&Ok(empty)), "Sin cámara configurada");
    }
}
