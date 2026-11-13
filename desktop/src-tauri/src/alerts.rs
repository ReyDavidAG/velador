use std::sync::Mutex;

use tauri::{AppHandle, Manager};
use tauri_plugin_notification::NotificationExt;

use crate::engine::Summary;
use crate::text;

pub const AFTER_MS: i64 = 30_000;

#[derive(Default)]
pub struct GapWatch {
    told_since: Option<i64>,
    seen_event: Option<i64>,
}

#[derive(Debug, PartialEq)]
pub enum Notice {
    Cut { cause: String },
    Back { lasted_s: i64 },
}

impl GapWatch {
    pub fn step(&mut self, gap_start_ms: Option<i64>, cause: &str, now_ms: i64) -> Option<Notice> {
        match (gap_start_ms, self.told_since) {
            (Some(start), None) if now_ms - start >= AFTER_MS => {
                self.told_since = Some(start);
                Some(Notice::Cut {
                    cause: cause.to_string(),
                })
            }
            (None, Some(start)) => {
                self.told_since = None;
                Some(Notice::Back {
                    lasted_s: (now_ms - start) / 1000,
                })
            }
            _ => None,
        }
    }
}

impl GapWatch {
    pub fn person(&mut self, event: Option<(i64, &str)>) -> bool {
        let Some((id, kind)) = event else {
            return false;
        };
        let first = self.seen_event.is_none();
        let new = self.seen_event != Some(id);
        self.seen_event = Some(id);
        new && !first && kind == "person"
    }
}

pub struct Alerts(pub Mutex<GapWatch>);

pub fn check(app: &AppHandle, summary: &Summary) {
    let Some(camera) = summary.cameras.first() else {
        return;
    };
    let gap = camera.gap.as_ref();
    let person = app
        .state::<Alerts>()
        .0
        .lock()
        .ok()
        .is_some_and(|mut watch| {
            watch.person(camera.last_event.as_ref().map(|e| (e.id, e.kind.as_str())))
        });
    if person {
        let _ = app
            .notification()
            .builder()
            .title("Persona detectada")
            .body(format!("{}: la cámara vio a una persona.", camera.name))
            .show();
    }
    let notice = app.state::<Alerts>().0.lock().ok().and_then(|mut watch| {
        watch.step(
            gap.map(|g| g.start_ms),
            gap.map(|g| g.cause.as_str()).unwrap_or(""),
            summary.now_ms,
        )
    });
    let (title, body) = match notice {
        Some(Notice::Cut { cause }) => (
            "Hueco en la grabación",
            format!("{}: {}.", camera.name, text::cause(&cause)),
        ),
        Some(Notice::Back { lasted_s }) => (
            "De guardia otra vez",
            format!(
                "{} volvió. El hueco duró {}.",
                camera.name,
                text::span(lasted_s)
            ),
        ),
        None => return,
    };
    let _ = app.notification().builder().title(title).body(body).show();
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn avisa_a_los_30_s_y_una_sola_vez() {
        let mut watch = GapWatch::default();
        assert_eq!(watch.step(Some(0), "camera", 10_000), None);
        assert_eq!(
            watch.step(Some(0), "camera", 31_000),
            Some(Notice::Cut {
                cause: "camera".into()
            })
        );
        assert_eq!(watch.step(Some(0), "camera", 90_000), None);
    }

    #[test]
    fn avisa_cuando_vuelve_con_lo_que_duro() {
        let mut watch = GapWatch::default();
        watch.step(Some(0), "camera", 31_000);
        assert_eq!(
            watch.step(None, "", 96_000),
            Some(Notice::Back { lasted_s: 96 })
        );
        assert_eq!(watch.step(None, "", 97_000), None);
    }

    #[test]
    fn avisa_de_una_persona_nueva_pero_no_de_lo_que_ya_estaba() {
        let mut watch = GapWatch::default();
        assert!(!watch.person(Some((7, "person"))));
        assert!(!watch.person(Some((7, "person"))));
        assert!(!watch.person(Some((8, "motion"))));
        assert!(watch.person(Some((9, "person"))));
        assert!(!watch.person(None));
    }

    #[test]
    fn un_hueco_corto_no_avisa_ni_al_irse() {
        let mut watch = GapWatch::default();
        assert_eq!(watch.step(Some(0), "engine", 5_000), None);
        assert_eq!(watch.step(None, "", 8_000), None);
    }
}
