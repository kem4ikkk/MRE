'use strict';
const own = document.getElementById('own');
const other = document.getElementById('other');
const status = document.getElementById('status');
const confirm = document.getElementById('confirm');
const cancel = document.getElementById('cancel');
const examples = {own:['Maplestrike ×1 · 100%','Перевязка ×2 · 100%'],other:['Timberwolf ×1 · 87%','Магазин ×1 · 100%']};
function fillSlot(slot, entry){
  if (!entry) { slot.textContent='Пустая ячейка'; slot.classList.add('empty'); return; }
  slot.textContent=entry;
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
