class AddSymbolSize < ActiveRecord::Migration[5.2]
  def change
    add_column :debug_symbols, :size, :integer
  end
end
