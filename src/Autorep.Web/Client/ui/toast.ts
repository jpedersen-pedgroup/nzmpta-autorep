// Lightweight toast notifications (plain DOM — no framework needed). Used for action-triggered
// signals such as a failed sync, on top of the persistent connectivity badge.
type ToastType = "info" | "success" | "error";

export function showToast(message: string, type: ToastType = "info", durationMs = 4500): void {
  let container = document.querySelector<HTMLDivElement>(".toast-container");
  if (!container) {
    container = document.createElement("div");
    container.className = "toast-container";
    document.body.appendChild(container);
  }

  const el = document.createElement("div");
  el.className = `toast toast--${type}`;
  el.setAttribute("role", "status");
  el.textContent = message;
  container.appendChild(el);

  // Next frame so the enter transition runs.
  requestAnimationFrame(() => el.classList.add("toast--show"));

  setTimeout(() => {
    el.classList.remove("toast--show");
    setTimeout(() => el.remove(), 250);
  }, durationMs);
}
