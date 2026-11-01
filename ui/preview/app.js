"use strict";
// Только интерактивный предпросмотр: игровые данные передаёт серверный MreHud.
const byId = id => document.getElementById(id);
const state = { phase: "incoming", role: "recipient", deadline: 0, health: 96, ammo: 24, reserve: 90, reloading: false, moving: false, closedAt: 0 };
const timers = { toast: 0, reload: 0, building: 0 };
const held = new Set();
const requestDuration = 90000;
let lastTick = performance.now();
let stamina = 100;
const vote = { phase: "active", yes: 2, required: 3, eligible: 5, duration: 60000, deadline: 0, until: 0, hasVoted: false };

function startVote() {
  vote.phase = "active"; vote.yes = 2; vote.required = 3; vote.hasVoted = false;
  vote.deadline = performance.now() + vote.duration; vote.until = 0; renderVote(performance.now());
}
function voteYes() {
  if (vote.phase !== "active" || vote.hasVoted || performance.now() >= vote.deadline) return;
  vote.hasVoted = true; vote.yes++;
  if (vote.yes >= vote.required) { vote.phase = "passed"; vote.until = performance.now() + 8000; }
  renderVote(performance.now());
}
function renderVote(now) {
  if (vote.phase === "active" && now >= vote.deadline) { vote.phase = "expired"; vote.until = now + 8000; }
  const ended = vote.phase !== "active";
  byId("vote-panel").hidden = ended && now >= vote.until;
  byId("vote-panel").classList.toggle("vote-failed", vote.phase === "expired");
  byId("vote-timer").textContent = ended ? "ЗАВЕРШЕНО" : Math.ceil(Math.max(0, vote.deadline - now) / 1000) + " с";
  byId("vote-topic").textContent = vote.phase === "passed" ? "Голосование принято" : vote.phase === "expired" ? "Голосование завершено" : "Наступление дня";
  byId("vote-initiator").textContent = "Начал Сталкер · участников " + vote.eligible;
  byId("vote-count").replaceChildren(document.createTextNode(String(vote.yes) + " "), Object.assign(document.createElement("span"), {textContent:"/ " + vote.required}));
  byId("vote-status").textContent = vote.phase === "passed" ? "Наступил день" : vote.phase === "expired" ? "Не хватило голосов" : "Нужен ещё " + Math.max(0, vote.required - vote.yes) + " голос";
  byId("vote-fill").style.width = Math.min(100, vote.yes / vote.required * 100) + "%";
  byId("vote-hint").textContent = ended ? "Результат голосования" : vote.hasVoted ? "Ваш голос уже принят" : "Поддержать: /vote yes или /cv";
}

function plantTrees() {
  const forest = byId("forest");
  let seed = 53;
  const random = () => { seed = (seed * 16807) % 2147483647; return seed / 2147483647; };
  for (let i = 0; i < 62; i++) {
    const x = random() * 2100 - 90;
    const base = 550 + random() * 130;
    const height = 100 + (base - 550) * 1.65 + random() * 110;
    if (x > 820 && x < 1290) continue;
    const tree = document.createElementNS("http://www.w3.org/2000/svg", "use");
    tree.setAttribute("href", "#pine");
    tree.setAttribute("x", String(x)); tree.setAttribute("y", String(base - height));
    tree.setAttribute("width", String(height * .47)); tree.setAttribute("height", String(height));
    tree.setAttribute("opacity", String(.7 + random() * .3)); forest.append(tree);
  }
}
function notify(message) {
  clearTimeout(timers.toast); byId("toast").textContent = message; byId("toast").hidden = false;
  timers.toast = setTimeout(() => { byId("toast").hidden = true; }, 3000);
}
function updateAmmo() {
  byId("ammo-value").textContent = String(state.ammo).padStart(2, "0");
  byId("capacity-value").textContent = "30";
  byId("ammo-value").style.color = state.ammo <= 5 ? "var(--red)" : "";
  byId("reload-hint").textContent = state.reloading ? "ПЕРЕЗАРЯДКА…" : "R · Перезарядить";
  byId("ammo-segments").replaceChildren(...Array.from({ length: 30 }, (_, i) => {
    const segment = document.createElement("i"); if (i < state.ammo) segment.className = "loaded"; return segment;
  }));
}
function setHealth(value) {
  state.health = Math.max(1, Math.min(100, Number(value)));
  byId("health-value").textContent = String(state.health); byId("health-fill").style.width = state.health + "%";
  byId("health-range").value = String(state.health); byId("health-output").textContent = state.health + "%";
  byId("health-panel").classList.toggle("danger", state.health <= 30);
  byId("condition").textContent = state.health <= 30 ? "НУЖНА МЕДИЦИНСКАЯ ПОМОЩЬ" : state.health <= 65 ? "ЕСТЬ РАНЕНИЯ" : "СОСТОЯНИЕ СТАБИЛЬНО";
}
function renderRequest() {
  const incoming = state.phase === "incoming";
  const waiting = state.phase === "waiting";
  const counting = state.phase === "countdown";
  byId("teleport").hidden = state.phase === "idle";
  byId("teleport-actions").hidden = !incoming;
  byId("countdown").hidden = !counting;
  byId("progress-track").hidden = !counting;
  byId("cancel-transfer").hidden = !counting && !waiting;
  byId("request-expiry").hidden = counting;
  byId("teleport-icon").textContent = counting ? "◎" : "↗";
  if (incoming) {
    byId("teleport-title").textContent = "К вам хотят переместиться";
    byId("teleport-description").replaceChildren(Object.assign(document.createElement("strong"), {textContent:"Сталкер"}), document.createTextNode(" отправил запрос на телепортацию"));
    byId("teleport-hint").textContent = "Игрок появится рядом с вами через 5 секунд после согласия";
  } else if (waiting) {
    byId("teleport-title").textContent = "Запрос отправлен";
    byId("teleport-description").textContent = "Ожидаем согласия игрока Сталкер";
    byId("teleport-hint").textContent = "Можно продолжать играть. Запрос действует 90 секунд.";
  } else if (counting) {
    byId("teleport-title").textContent = state.role === "home" ? "Перемещение домой" : state.role === "sender" ? "Перемещение к Сталкеру" : "Сталкер перемещается к вам";
    byId("teleport-description").textContent = state.role === "home" ? "Дом «Основная база» · телепортация готовится" : "Запрос принят · телепортация готовится";
    byId("teleport-hint").textContent = "Бой, рейд, смерть или выход игрока отменят перемещение";
  }
}
function request(role = "recipient") {
  clearTimeout(timers.toast); byId("toast").hidden = true;
  state.role = role; state.phase = role === "recipient" ? "incoming" : "waiting";
  state.deadline = performance.now() + requestDuration; renderRequest(); tick(performance.now());
  if (role === "sender") notify("Для примера получатель примет запрос через 2 секунды");
}
function accept(at = performance.now()) {
  if (!["incoming", "waiting"].includes(state.phase)) return;
  if (at >= state.deadline) { cancel("Время запроса истекло"); return; }
  state.phase = "countdown"; state.deadline = at + 5000; renderRequest();
  byId("countdown-number").textContent = "5";
  byId("progress-fill").style.width = "0%";
}
function home() {
  clearTimeout(timers.toast); byId("toast").hidden = true;
  state.role = "home"; state.phase = "countdown"; state.deadline = performance.now() + 5000;
  renderRequest(); byId("countdown-number").textContent = "5"; byId("progress-fill").style.width = "0%";
}
function cancel(message = "Запрос отклонён") {
  if (state.phase === "idle") return;
  state.phase = "idle"; renderRequest(); notify(message);
}
function shoot() {
  if (state.reloading) return;
  if (state.ammo === 0) { notify("Магазин пуст · R — перезарядить"); return; }
  state.ammo--; updateAmmo();
}
function reload() {
  if (state.reloading || state.ammo === 30 || state.reserve === 0) return;
  state.reloading = true; updateAmmo();
  timers.reload = setTimeout(() => {
    const bullets = Math.min(30 - state.ammo, state.reserve); state.ammo += bullets; state.reserve -= bullets;
    state.reloading = false; updateAmmo();
  }, 1800);
}
function damage() {
  setHealth(state.health - 24); byId("game").classList.remove("hit");
  void byId("game").offsetWidth; byId("game").classList.add("hit");
  if (state.phase !== "idle") cancel("Перемещение отменено: вы получили урон");
}
function reset() {
  clearTimeout(timers.reload); clearTimeout(timers.toast); state.reloading = false;
  state.ammo = 24; state.reserve = 90; stamina = 100; held.clear(); state.moving = false;
  byId("toast").hidden = true; byId("game").classList.remove("moving", "hit");
  setHealth(96); updateAmmo(); request();
}
function tick(now) {
  renderVote(now);
  const elapsed = Math.max(0, Math.min(.1, (now - lastTick) / 1000)); lastTick = now;
  if (state.phase === "waiting" && now >= state.deadline - requestDuration + 2000) accept(now);
  const remaining = Math.max(0, state.deadline - now);
  if (state.phase === "countdown") {
    byId("countdown-number").textContent = String(Math.ceil(remaining / 1000));
    byId("progress-fill").style.width = Math.max(0, Math.min(100, 100 - remaining / 50)) + "%";
    if (remaining === 0) { const role = state.role; state.phase = "idle"; renderRequest(); notify(role === "home" ? "Вы переместились домой" : role === "sender" ? "Вы переместились к Сталкеру" : "Сталкер переместился к вам"); }
  } else if (state.phase !== "idle") {
    const seconds = Math.ceil(remaining / 1000);
    byId("request-expiry").textContent = String(Math.floor(seconds / 60)).padStart(2, "0") + ":" + String(seconds % 60).padStart(2, "0");
    if (remaining === 0) cancel("Время запроса истекло");
  }
  state.moving = [...held].some(key => ["KeyW", "KeyA", "KeyS", "KeyD"].includes(key));
  byId("game").classList.toggle("moving", state.moving);
  byId("movement-status").textContent = state.moving ? "В ДВИЖЕНИИ" : "НА МЕСТЕ";
  stamina = Math.min(100, Math.max(0, stamina + elapsed * (state.moving && held.has("ShiftLeft") ? -12 : 9)));
  byId("stamina-value").textContent = Math.round(stamina) + "%";
  byId("stamina-fill").style.width = stamina + "%";
}
function animate(now) { tick(now); requestAnimationFrame(animate); }
byId("accept-button").addEventListener("click", () => accept());
byId("deny-button").addEventListener("click", () => cancel());
byId("cancel-transfer").addEventListener("click", () => cancel("Перемещение отменено"));
byId("incoming-button").addEventListener("click", () => request());
byId("outgoing-button").addEventListener("click", () => request("sender"));
byId("home-button").addEventListener("click", home);
byId("shoot-button").addEventListener("click", shoot);
byId("reload-button").addEventListener("click", reload);
byId("damage-button").addEventListener("click", damage);
byId("building-button").addEventListener("click", () => {
  clearTimeout(timers.building); byId("building-panel").hidden = false;
  timers.building = setTimeout(() => { byId("building-panel").hidden = true; }, 5000);
});
byId("reset-button").addEventListener("click", reset);
byId("vote-start-button").addEventListener("click", startVote);
byId("vote-yes-button").addEventListener("click", voteYes);
byId("health-range").addEventListener("input", event => setHealth(event.target.value));
function toggleTools(open) { byId("tools").hidden = !open; byId("tools-toggle").setAttribute("aria-expanded", String(open)); }
byId("tools-toggle").addEventListener("click", () => toggleTools(byId("tools").hidden));
byId("tools-close").addEventListener("click", () => toggleTools(false));
window.addEventListener("keydown", event => {
  if (event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement || event.target.isContentEditable) return;
  held.add(event.code); if (event.repeat) return;
  if (event.code === "Comma") { event.preventDefault(); if (state.phase === "incoming") accept(); }
  else if (event.code === "Period") { event.preventDefault(); cancel(state.phase === "incoming" ? "Запрос отклонён" : "Перемещение отменено"); }
  else if (event.code === "KeyR") reload();
  else if (event.code === "Escape") toggleTools(false);
});
window.addEventListener("keyup", event => held.delete(event.code));
window.addEventListener("blur", () => held.clear());
plantTrees(); setHealth(96); updateAmmo(); request(); startVote(); requestAnimationFrame(animate);
