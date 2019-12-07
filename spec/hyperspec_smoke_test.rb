# frozen_string_literal: true

# Smoke test from: https://docs.hyperstack.org/tools/hyper-spec
require 'rails_helper'

describe 'Hyperspec', js: true do
  it 'can mount and test a component' do
    mount 'HyperSpecTest' do
      class HyperSpecTest < HyperComponent
        render(DIV) do
          "It's Alive!"
        end
      end
    end
    expect(page).to have_content("It's Alive!")
  end
  it 'can evaluate and test expressions on the client' do
    expect_evaluate_ruby do
      [1, 2, 3].reverse
    end.to eq [3, 2, 1]
  end
end
