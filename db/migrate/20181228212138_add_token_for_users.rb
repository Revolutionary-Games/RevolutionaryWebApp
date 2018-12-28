class AddTokenForUsers < ActiveRecord::Migration[5.2]
  def change
    add_column :users, :api_token, :string
    add_index :users, :api_token, unique: true
  end
end
