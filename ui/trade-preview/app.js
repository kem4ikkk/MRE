'use strict';
const own = document.getElementById('own');
const other = document.getElementById('other');
const status = document.getElementById('status');
const confirm = document.getElementById('confirm');
const cancel = document.getElementById('cancel');
const examples = {own:['Maplestrike ×1 · 100%','Перевязка ×2 · 100%'],other:['Timberwolf ×1 · 87%','Магазин ×1 · 100%']};
let active = true;
function reset(){
  active=true; confirm.disabled=cancel.disabled=false; own.replaceChildren(); other.replaceChildren();
  for(let i=0;i<10;i++){
    const a=document.createElement('button');a.type='button';a.className='slot';a.textContent=examples.own[i]||'Пустая ячейка';a.setAttribute('aria-label',`Ваша ячейка ${i+1}: ${a.textContent}`);
    a.addEventListener('click',()=>{if(active){status.textContent='Демонстрация: здесь открывается штатный инвентарь. Согласия сбрасываются при изменении предложения.';confirm.disabled=false;}});own.append(a);
    const b=document.createElement('div');b.className='slot';b.textContent=examples.other[i]||'Пустая ячейка';other.append(b);
  }
  status.textContent='Обе стороны проверяют предметы. Подтверждений: 0 / 2.';
}
confirm.addEventListener('click',()=>{if(active){status.textContent='Ваше подтверждение принято. Ожидание партнёра — 1 / 2. Предметы ещё не переданы.';confirm.disabled=true;}});
cancel.addEventListener('click',()=>{active=false;confirm.disabled=cancel.disabled=true;status.textContent='Демонстрационный обмен отменён. Игровые предметы не изменялись.';});
document.getElementById('reset').addEventListener('click',reset);reset();
