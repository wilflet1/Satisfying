import { CFG } from './config';
import type { Hud } from './game';

/**
 * The HUD is DOM, not canvas. Text rendered by the browser stays crisp at every
 * DPR for free, and it keeps the fragment shader focused on the one thing it's
 * expensive at.
 */
export class Ui {
  private root = document.getElementById('ui') as HTMLDivElement;
  private score = document.getElementById('score') as HTMLDivElement;
  private mult = document.getElementById('mult') as HTMLDivElement;
  private massFill = document.getElementById('mass-fill') as HTMLDivElement;
  private pips = document.getElementById('pips') as HTMLDivElement;
  private title = document.getElementById('title') as HTMLDivElement;
  private over = document.getElementById('over') as HTMLDivElement;
  private finalScore = document.getElementById('final-score') as HTMLDivElement;
  private finalBest = document.getElementById('final-best') as HTMLDivElement;
  private newBest = document.getElementById('new-best') as HTMLDivElement;
  private titleBest = document.getElementById('title-best') as HTMLDivElement;

  private lastState = '';
  private lastCount = -1;
  private lastCombo = -1;

  constructor() {
    for (let i = 0; i < CFG.maxBlobs; i++) {
      const p = document.createElement('span');
      p.className = 'pip';
      this.pips.appendChild(p);
    }
  }

  update(h: Hud) {
    this.score.textContent = `${h.score}`;

    if (h.combo !== this.lastCombo) {
      this.lastCombo = h.combo;
      this.mult.textContent = h.multiplier > 1.001 ? `×${h.multiplier.toFixed(2)}` : '';
      this.mult.classList.toggle('hot', h.multiplier >= 2);
      if (h.combo > 0) {
        this.mult.classList.remove('bump');
        void this.mult.offsetWidth; // restart the CSS animation
        this.mult.classList.add('bump');
      }
    }

    this.massFill.style.transform = `scaleX(${h.massFrac.toFixed(3)})`;
    this.massFill.classList.toggle('low', h.massFrac < 0.22);

    if (h.count !== this.lastCount) {
      this.lastCount = h.count;
      const kids = this.pips.children;
      for (let i = 0; i < kids.length; i++) kids[i].classList.toggle('on', i < h.count);
    }

    if (h.state !== this.lastState) {
      this.lastState = h.state;
      this.root.dataset.state = h.state;
      this.title.classList.toggle('show', h.state === 'title');
      this.over.classList.toggle('show', h.state === 'dead');
      this.titleBest.textContent = h.best > 0 ? `BEST ${h.best} m` : '';
      if (h.state === 'dead') {
        this.finalScore.textContent = `${h.score}`;
        this.finalBest.textContent = `BEST ${h.best} m`;
        this.newBest.classList.toggle('show', h.score >= h.best && h.score > 0);
      }
    }
  }
}
