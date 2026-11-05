'use strict';
const own = document.getElementById('own');
const other = document.getElementById('other');
const status = document.getElementById('status');
const confirm = document.getElementById('confirm');
const cancel = document.getElementById('cancel');
// Условные пиктограммы показывают расположение изображений в макете, а не игровые ассеты.
const icons = {
  rifle:'<path d="M3 11h5l2-3h11l2 2h7v3h-8l-1 2h-4l-1 5h-4l1-5H8l-1 2H4z"/>',
  bandage:'<rect x="5" y="7" width="22" height="18" rx="3"/><path class="cut" d="M14 10h4v4h4v4h-4v4h-4v-4h-4v-4h4z"/>',
  sniper:'<path d="M2 13h8l2-3h8l2 2h8v2H19l-1 3h-4l-1 3H9l1-4H5z"/><path d="M13 7h7v2h-7z"/>',
  magazine:'<path d="M11 4h11l-1 8 2 13-3 3H9l-1-3 2-13z"/><path class="cut" d="M12 8h8v2h-8z"/>'
};
const examples = {own:[['Maplestrike ×1 · 100%','rifle'],['Перевязка ×2 · 100%','bandage']],other:[['Timberwolf ×1 · 87%','sniper'],['Магазин ×1 · 100%','magazine']]};
function fillSlot(slot, entry){
  if (!entry) { slot.textContent='Пустая ячейка'; slot.classList.add('empty'); return; }
  const icon=document.createElement('span'); icon.className='item-icon'; icon.setAttribute('aria-hidden','true');
  icon.innerHTML=`<svg viewBox="0 0 32 32" focusable="false">${icons[entry[1]]}</svg>`;
  const label=document.createElement('span'); label.className='item-label'; label.textContent=entry[0];
  slot.append(icon,label);
}
let active = true;
function reset(){
  active=true; confirm.disabled=cancel.disabled=false; own.replaceChildren(); other.replaceChildren();
  for(let i=0;i<10;i++){
    const a=document.createElement('button');a.type='button';a.className='slot';fillSlot(a,examples.own[i]);a.setAttribute('aria-label',`Ваша ячейка ${i+1}: ${a.textContent}`);
    a.addEventListener('click',()=>{if(active){status.textContent='Демонстрация: здесь открывается штатный инвентарь. Согласия сбрасываются при изменении предложения.';confirm.disabled=false;}});own.append(a);
    const b=document.createElement('div');b.className='slot';fillSlot(b,examples.other[i]);other.append(b);
  }
  status.textContent='Обе стороны проверяют предметы. Подтверждений: 0 / 2.';
}
confirm.addEventListener('click',()=>{if(active){status.textContent='Ваше подтверждение принято. Ожидание партнёра — 1 / 2. Предметы ещё не переданы.';confirm.disabled=true;}});
cancel.addEventListener('click',()=>{active=false;confirm.disabled=cancel.disabled=true;status.textContent='Демонстрационный обмен отменён. Игровые предметы не изменялись.';});
document.getElementById('reset').addEventListener('click',reset);reset();
