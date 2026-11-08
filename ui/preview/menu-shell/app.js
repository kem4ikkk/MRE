'use strict';
// Только навигация макета. Игровые данные и действия не подключены.
const sections = [
  ['home','Главная'], ['help','Справка'], ['team','Команда'],
  ['events','Ивенты'], ['stats','Статистика'], ['links','Ссылки'],
  ['settings','Настройки'], ['battlepass','BattlePass'],
  ['rewards','Награды'], ['reports','Репорты']
];
const tabs = document.querySelector('.tabs');
const pages = document.querySelector('.pages');
const menu = document.querySelector('.menu');
const bell = document.querySelector('#notifications-toggle');
const notifications = document.querySelector('#notifications');
const reopen = document.querySelector('#reopen-menu');
let selected = 0;
const icon = id => `<svg class="icon" aria-hidden="true"><use href="#icon-${id}"></use></svg>`;
sections.forEach(([id,label],index) => {
  const tab = document.createElement('button');
  tab.type = 'button';
  tab.className = 'tab';
  tab.id = `tab-${id}`;
  tab.setAttribute('role','tab');
  tab.setAttribute('aria-controls',`page-${id}`);
  tab.innerHTML = `${icon(id)}<span>${label}</span>`;
  tab.addEventListener('click', () => select(index));
  tabs.append(tab);
  const page = document.createElement('section');
  page.className = 'page';
  page.id = `page-${id}`;
  page.setAttribute('role','tabpanel');
  page.setAttribute('aria-labelledby',tab.id);
  page.tabIndex = 0;
  page.innerHTML = `<div class="page-heading">${icon(id)}<h1>${label}</h1></div><div class="empty-page"></div>`;
  pages.append(page);
});
function select(index, focus = false) {
  selected = (index + sections.length) % sections.length;
  [...tabs.children].forEach((tab,i) => {
    const active = i === selected;
    tab.setAttribute('aria-selected',String(active));
    tab.tabIndex = active ? 0 : -1;
    pages.children[i].hidden = !active;
  });
  const tab = tabs.children[selected];
  tab.scrollIntoView({block:'nearest',inline:'nearest'});
  if (focus) tab.focus({preventScroll:true});
  dismissNotifications();
}
tabs.addEventListener('keydown', event => {
  let index;
  if(event.key === 'ArrowRight') index = selected + 1;
  if(event.key === 'ArrowLeft') index = selected - 1;
  if(event.key === 'Home') index = 0;
  if(event.key === 'End') index = sections.length - 1;
  if(index === undefined) return;
  event.preventDefault();
  select(index,true);
});
document.querySelector('#previous-tab').addEventListener('click', () => select(selected-1));
document.querySelector('#next-tab').addEventListener('click', () => select(selected+1));
function dismissNotifications(restoreFocus = false) {
  notifications.hidden = true;
  bell.setAttribute('aria-expanded','false');
  if(restoreFocus) bell.focus();
}
bell.addEventListener('click', () => {
  const opening = notifications.hidden;
  notifications.hidden = !opening;
  bell.setAttribute('aria-expanded',String(opening));
  if(opening) document.querySelector('#close-notifications').focus();
});
document.querySelector('#close-notifications').addEventListener('click', () => dismissNotifications(true));
document.addEventListener('click',event => {
  if(!notifications.hidden && !notifications.contains(event.target) && !bell.contains(event.target)) dismissNotifications();
});
function closeMenu() {
  dismissNotifications();
  menu.hidden = true;
  reopen.hidden = false;
  document.body.classList.add('closed');
  reopen.focus();
}
document.querySelector('#close-menu').addEventListener('click',closeMenu);
reopen.addEventListener('click', () => {
  menu.hidden = false;
  reopen.hidden = true;
  document.body.classList.remove('closed');
  tabs.children[selected].focus();
});
document.addEventListener('keydown',event => {
  if(event.key !== 'Escape' || menu.hidden) return;
  if(!notifications.hidden) dismissNotifications(true);
  else closeMenu();
});
// /help в будущем откроет команды внутри «Справки».
const entry = new URLSearchParams(location.search).get('section');
const entryIndex = sections.findIndex(([id]) => id === entry);
select(entryIndex < 0 ? 0 : entryIndex);
