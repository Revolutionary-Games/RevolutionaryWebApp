class AddHasForumAccountToPatron < ActiveRecord::Migration[5.2]
  def change
    add_column :patrons, :has_forum_account, :boolean
  end
end
